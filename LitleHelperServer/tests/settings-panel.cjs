const fs = require('fs'), vm = require('vm'), assert = require('assert'), path = require('path');
const source = fs.readFileSync(path.join(__dirname, '../wwwroot/app.js'), 'utf8');

const elements = new Map();
function element(id) {
  if (!elements.has(id)) {
    elements.set(id, {
      value: '',
      checked: false,
      innerHTML: '',
      textContent: '',
      disabled: false,
      hidden: false,
      isConnected: true,
      open: true,
      dataset: {},
      style: {},
      handlers: {},
      elements: new Proxy({}, { get: (target, prop) => element(String(prop)) }),
      addEventListener(event, fn) { this.handlers[event] = fn; },
      querySelectorAll(selector) {
        if (selector === '[data-settings-tab]') {
          return [...(this.innerHTML.matchAll(/data-settings-tab="([a-z-]+)"/g))].map(m => ({
            dataset: { settingsTab: m[1] },
            addEventListener(e, fn) { }
          }));
        }
        return [];
      },
      querySelector(s) { return element(s); }
    });
  }
  return elements.get(id);
}

const storage = new Map();
const localStorageMock = {
  getItem: k => storage.get(k) || null,
  setItem: (k, v) => storage.set(k, String(v))
};

const permissions = ['settings.manage', 'updates.manage', 'commands.execute'];
const context = vm.createContext({
  document: { querySelector: element, querySelectorAll: () => [] },
  $: element,
  can: key => permissions.includes(key),
  manage: () => true,
  superAdmin: () => false,
  roleName: r => r,
  toast: () => {},
  modal: () => {},
  closeModal: () => {},
  confirm: () => true,
  localStorage: localStorageMock,
  sessionStorage: { getItem: () => null, setItem: () => {}, removeItem: () => {} },
  window: { location: { origin: 'http://127.0.0.1:21500' } },
  Date, JSON, Number, String, Promise, Set, Map, Array,
  fetch: async (url, opts) => {
    const p = String(url).replace('/api', '');
    let result = {};
    if (p === '/settings/medical-statuses') result = [{id:1,key:'Available',title:'На приёме',emoji:'🟢',colorHex:'#16A34A',isActive:true,orderIndex:0}];
    else if (p === '/settings/emergency') result = { enabled: true, serverUrl: 'http://test:8085', apiKey: 'key', allowStandalone: true, allowClientTrigger: true, departments: [] };
    else if (p === '/settings/cartridge') result = { enabled: true, apiKey: 'cart-key' };
    else if (p === '/settings/glpi') result = { baseUrl: 'http://glpi.test', authMode: 'token', hasUserToken: true, serviceUserId: 1 };
    else if (p === '/settings/ad') result = { enabled: true, host: 'dc.test', port: 636, domain: 'test.local', netbiosDomain: 'TEST', baseDn: 'DC=test,DC=local', caCertificate: '' };
    else if (p === '/settings/telegram') result = { settings: { enabled: true, chatId: '-100', threadId: 0, hasBotToken: true } };
    else if (p === '/branches') result = [{ id: 1, name: 'Главный филиал', isActive: true }, { id: 2, name: 'Второй филиал', isActive: true }];
    else if (p === '/settings/messenger') result = { enabled: true, retentionDays: 90, storedMessages: 42 };
    else if (p === '/settings/updates') result = { enabled: true, latest: { version: '1.2.31', size: 69000000 } };
    else if (p === '/computers') result = [];
    return {
      ok: true,
      status: 200,
      json: async () => result
    };
  }
});

// Run app.js
vm.runInContext(source, context);

(async () => {
  // Authenticate user
  await vm.runInContext(`(async () => {
    acceptLogin({ token: 'test-token', user: { role: 'SuperAdmin', effectivePermissions: ['settings.manage', 'updates.manage', 'commands.execute'] } });
    globalThis.switchTab = async tab => { settingsTab = tab; await loadPage(); };
    page = 'settings';
    await loadPage();
  })()`, context);

  const content = element('#content').innerHTML;
  assert(content.includes('settings-tabs'), 'must render settings-tabs');
  assert(content.includes('data-settings-tab="ad"'), 'must include ad tab');
  assert(content.includes('data-settings-tab="telegram"'), 'must include telegram tab');
  assert(content.includes('data-settings-tab="glpi"'), 'must include glpi tab');
  assert(content.includes('data-settings-tab="cartridge"'), 'must include cartridge tab');
  assert(content.includes('data-settings-tab="emergency"'), 'must include emergency tab');
  assert(content.includes('data-settings-tab="branches"'), 'must include branches tab');
  assert(content.includes('data-settings-tab="messenger"'), 'must include messenger tab');
  assert(content.includes('data-settings-tab="updates"'), 'must include updates tab');

  assert(content.includes('data-settings-tab="medical-statuses"'), 'must include medical statuses');
  await context.switchTab('medical-statuses');
  assert(element('#settings-tab-body').innerHTML.includes('Available'), 'medical statuses render');
  // Switch to emergency tab
  await context.switchTab('emergency');
  const body = element('#settings-tab-body').innerHTML;
  assert(body.includes('AudioRONGTA'), 'emergency panel must render AudioRONGTA section');
  assert(body.includes('dept-list'), 'emergency panel must render department list');
  assert(body.includes('manual-alert-code'), 'emergency panel must render manual alert selector');
  assert(body.includes('telegramAlertsEnabled'), 'emergency panel must render telegram alerts toggle');
  assert(body.includes('emergency-matrix-card'), 'emergency panel must render structured emergency-matrix-card');
  assert(body.includes('data-code-enabled="CODE_RED"'), 'emergency matrix must render CODE_RED row');
  assert(body.includes('open-guard-white-btn'), 'emergency panel must render guard-white dedicated card');
  assert(!body.includes('<p><a href="/guard-white.html" target="_blank">🛡️ Экран поста охраны «Код Белый»</a></p>'), 'emergency panel must not have top duplicate guard-white link');

  // Switch to cartridge tab
  await context.switchTab('cartridge');
  const cartBody = element('#settings-tab-body').innerHTML;
  assert(cartBody.includes('cartridge-form'), 'cartridge panel must render cartridge form');
  assert(cartBody.includes('/api/integrations/cartridges/ready'), 'cartridge panel must render webhook endpoint');

  // Switch to glpi tab
  await context.switchTab('glpi');
  const glpiBody = element('#settings-tab-body').innerHTML;
  assert(glpiBody.includes('glpi-settings-form'), 'glpi panel must render form');

  // Switch to updates tab
  await context.switchTab('updates');
  const updatesBody = element('#settings-tab-body').innerHTML;
  assert(updatesBody.includes('client-update-form'), 'updates panel must render client-update-form');

  // Test userBranchLabels and userForm branch checkboxes
  let lastModal = '';
  context.modal = (title, html) => { lastModal = html || ''; };
  const mockBranches = [
    { id: 1, name: 'Поликлиника', isActive: true },
    { id: 2, name: 'Больница', isActive: true },
    { id: 3, name: 'Роддом', isActive: false }
  ];
  const labelAdminAll = context.userBranchLabels({ role: 'Admin', branchIds: [] }, mockBranches);
  assert(labelAdminAll.includes('Все филиалы'), 'admin with empty branches has Все филиалы');
  const labelAdminTwo = context.userBranchLabels({ role: 'Admin', branchIds: [1, 2] }, mockBranches);
  assert(labelAdminTwo.includes('Поликлиника, Больница'), 'admin with 2 branches renders both branch names');

  await context.userForm({ id: 10, username: 'admin_test', role: 'Admin', branchIds: [1, 2], room: '101' });
  assert(lastModal.includes('id="user-branches-fieldset"'), 'userForm must include user-branches-fieldset');
  assert(lastModal.includes('name="userBranchItem"'), 'userForm must render branch checkboxes');
  assert(lastModal.includes('btn-select-all-branches'), 'userForm must render select-all button');
  assert(lastModal.includes('btn-clear-all-branches'), 'userForm must render clear-all button');
  assert(lastModal.includes('value="1" checked'), 'userForm must check assigned branch 1');
  assert(lastModal.includes('value="2" checked'), 'userForm must check assigned branch 2');

  console.log('PASS unified settings panel: all 9 tabs render cleanly with correct permissions and forms');
  console.log('PASS user branch checkboxes: multi-branch selection and labels verified');
})().catch(err => {
  console.error(err);
  process.exit(1);
});

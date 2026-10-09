'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

function check(condition, message) {
  if (!condition) {
    console.error('FAIL: ' + message);
    process.exit(1);
  }
  console.log('PASS ' + message);
}

const messengerJsPath = path.join(__dirname, '..', 'wwwroot', 'messenger', 'messenger.js');
const messengerHtmlPath = path.join(__dirname, '..', 'wwwroot', 'messenger', 'index.html');

check(fs.existsSync(messengerJsPath), 'messenger.js exists');
check(fs.existsSync(messengerHtmlPath), 'index.html exists');

const jsCode = fs.readFileSync(messengerJsPath, 'utf8');
const htmlCode = fs.readFileSync(messengerHtmlPath, 'utf8');

// Set up DOM mock environment
class MockStorage {
  constructor() { this.store = new Map(); }
  getItem(k) { return this.store.get(k) || null; }
  setItem(k, v) { this.store.set(k, String(v)); }
  removeItem(k) { this.store.delete(k); }
  clear() { this.store.clear(); }
}

const storage = new MockStorage();

class MockElement {
  constructor(tag) {
    this.tagName = tag.toUpperCase();
    this.children = [];
    this.childNodes = [];
    this.attributes = new Map();
    this.className = '';
    this.textContent = '';
    this.value = '';
    this.title = '';
    this.hidden = false;
    this.style = {};
    this.dataset = {};
    this.onclick = null;
    this.onchange = null;
    this.disabled = false;
    this.classList = {
      add: () => {},
      remove: () => {},
      toggle: () => {}
    };
  }
  setAttribute(k, v) { this.attributes.set(k, String(v)); }
  getAttribute(k) { return this.attributes.get(k) || null; }
  append(...nodes) {
    for (const n of nodes) {
      if (typeof n === 'string') {
        const textNode = { nodeType: 3, textContent: n };
        this.childNodes.push(textNode);
      } else {
        this.children.push(n);
        this.childNodes.push(n);
      }
    }
  }
  replaceChildren(...nodes) {
    this.children = [];
    this.childNodes = [];
    this.append(...nodes);
  }
  querySelector(sel) {
    return this.querySelectorAll(sel)[0] || null;
  }
  querySelectorAll(sel) {
    const results = [];
    function search(el) {
      if (!el.children) return;
      for (const c of el.children) {
        if (sel.startsWith('#') && c.id === sel.slice(1)) results.push(c);
        else if (sel.startsWith('.') && c.className.split(' ').includes(sel.slice(1))) results.push(c);
        else if (sel.toLowerCase() === c.tagName.toLowerCase()) results.push(c);
        else if (sel.includes('[data-filter="')) {
          const m = sel.match(/\[data-filter="([^"]+)"\]/);
          if (m && c.dataset.filter === m[1]) results.push(c);
        }
        search(c);
      }
    }
    search(this);
    return results;
  }
  close() {}
  showModal() {}
}

const doc = new MockElement('html');
doc.dataset.theme = 'dark';
const elementsById = new Map();

function registerElement(id, tag = 'div', className = '') {
  const el = new MockElement(tag);
  el.id = id;
  el.className = className;
  elementsById.set(id, el);
  doc.append(el);
  return el;
}

// Elements present in index.html
registerElement('sso', 'button');
registerElement('app', 'main');
const loginSec = registerElement('login', 'section');
const loginP = new MockElement('p');
loginSec.append(loginP);
const loginForm = registerElement('login-form', 'form');
const l1 = new MockElement('label');
l1.childNodes.push({ textContent: 'Учётная запись AD' });
const in1 = new MockElement('input');
l1.children.push(in1);
l1.childNodes.push(in1);
const l2 = new MockElement('label');
l2.childNodes.push({ textContent: 'Пароль' });
const in2 = new MockElement('input');
l2.children.push(in2);
l2.childNodes.push(in2);
const sBtn = new MockElement('button');
sBtn.className = 'secondary';
loginForm.append(l1, l2, sBtn);

registerElement('login-lang', 'button');
registerElement('login-error', 'p');
registerElement('me', 'button');
registerElement('broadcast', 'button');
registerElement('lang', 'button');
registerElement('theme', 'button');
registerElement('settings', 'button');

const dock = registerElement('dock', 'nav');
dock.className = 'dock';
['all', 'personal', 'groups', 'unread', 'favourite'].forEach(f => {
  const b = new MockElement('button');
  b.dataset.filter = f;
  dock.append(b);
});

const dialogs = registerElement('dialogs', 'aside');
dialogs.className = 'dialogs';
const dHeader = new MockElement('header');
const dStrong = new MockElement('strong');
dHeader.append(dStrong);
dialogs.append(dHeader);

registerElement('new-group', 'button');
registerElement('search', 'input');
registerElement('clear-search', 'button');

const filters = registerElement('filters', 'div');
filters.className = 'filters';
['all', 'unread', 'groups', 'personal'].forEach(f => {
  const b = new MockElement('button');
  b.dataset.filter = f;
  filters.append(b);
});

registerElement('chat-title', 'strong');
registerElement('chat-status', 'small');
registerElement('chat-avatar', 'div');
registerElement('back', 'button');
registerElement('chat-search', 'button');
registerElement('info', 'button');

const conv = registerElement('conversation', 'section');
conv.className = 'conversation';
const emptyDiv = new MockElement('div');
emptyDiv.className = 'empty';
const empH2 = new MockElement('h2');
const empP = new MockElement('p');
emptyDiv.append(empH2, empP);
conv.append(emptyDiv);

registerElement('new-messages', 'button');
registerElement('attach', 'button');
registerElement('file-input', 'input');
registerElement('input', 'textarea');
registerElement('emoji', 'button');
registerElement('send', 'button');

const drawer = registerElement('drawer', 'aside');
const drwHeader = new MockElement('header');
const drwStrong = new MockElement('strong');
drwHeader.append(drwStrong);
drawer.append(drwHeader);
registerElement('close-info', 'button');
registerElement('close-modal', 'button');
registerElement('modal', 'dialog');
registerElement('modal-title', 'strong');
registerElement('modal-body', 'div');
registerElement('contacts', 'div');
registerElement('feed', 'div');
registerElement('pending-files', 'div');
registerElement('emoji-picker', 'div');
registerElement('profile', 'div');
registerElement('connection', 'footer');
registerElement('toast', 'div');
registerElement('error', 'small');

const sandbox = {
  document: {
    documentElement: doc,
    title: '',
    getElementById(id) { return elementsById.get(id) || null; },
    querySelector(sel) { return doc.querySelector(sel); },
    querySelectorAll(sel) { return doc.querySelectorAll(sel); },
    createElement(tag) { return new MockElement(tag); },
    createTextNode(t) { return { nodeType: 3, textContent: t }; }
  },
  localStorage: storage,
  location: { origin: 'http://localhost', protocol: 'https:' },
  fetch: async () => ({ ok: false, json: async () => ({}) }),
  WebSocket: class { close() {} send() {} },
  setInterval: () => 0,
  clearInterval: () => {},
  setTimeout: (fn) => 0,
  clearTimeout: () => {},
  crypto: { randomUUID: () => 'uuid-123' },
  addEventListener: () => {},
  removeEventListener: () => {},
  console
};

sandbox.window = sandbox;
sandbox.globalThis = sandbox;

const context = vm.createContext(sandbox);

// Execute messenger.js in the mock sandbox
vm.runInContext(jsCode, context);

// 1. Verify I18N dictionary exists and contains 4 languages
const I18N = vm.runInContext('I18N', context);
check(I18N && typeof I18N === 'object', 'I18N object is declared');

const supportedLangs = ['ru', 'kk', 'en', 'zh'];
supportedLangs.forEach(lang => {
  check(I18N[lang] !== undefined, `I18N contains '${lang}' dictionary`);
  check(I18N[lang].flag !== undefined, `'${lang}' has a flag icon`);
  check(I18N[lang].lang_name !== undefined, `'${lang}' has a native language name`);
});

// 2. Verify all keys in ru are present in kk, en, zh
const ruKeys = Object.keys(I18N.ru);
check(ruKeys.length >= 70, `I18N has comprehensive key count: ${ruKeys.length} keys`);

for (const lang of ['kk', 'en', 'zh']) {
  const missingKeys = ruKeys.filter(k => I18N[lang][k] === undefined);
  check(missingKeys.length === 0, `'${lang}' has 100% key parity with 'ru' (missing: ${missingKeys.join(', ')})`);
}

// 3. Test language switching and reactivity
const setLang = vm.runInContext('setLang', context);
const currentLang = vm.runInContext('currentLang', context);
const t = vm.runInContext('t', context);
const getLocale = vm.runInContext('getLocale', context);

// Default is Russian
check(currentLang() === 'ru', 'default language is ru');
check(t('send') === 'Отправить', 'ru: t("send") is Отправить');
check(getLocale() === 'ru-RU', 'ru: locale is ru-RU');

// Switch to Kazakh
setLang('kk');
check(currentLang() === 'kk', 'currentLang() updated to kk');
check(storage.getItem('helper.chat.lang') === 'kk', 'localStorage holds kk');
check(t('send') === 'Жіберу', 'kk: t("send") is Жіберу');
check(getLocale() === 'kk-KZ', 'kk: locale is kk-KZ');
check(sandbox.document.title.includes('Хабарламалар'), 'document.title localized to Kazakh');
check(elementsById.get('login-lang').textContent.includes('Қазақша'), 'login-lang button shows Kazakh');

// Switch to English
setLang('en');
check(currentLang() === 'en', 'currentLang() updated to en');
check(storage.getItem('helper.chat.lang') === 'en', 'localStorage holds en');
check(t('send') === 'Send', 'en: t("send") is Send');
check(getLocale() === 'en-US', 'en: locale is en-US');
check(sandbox.document.title.includes('Messages'), 'document.title localized to English');
check(elementsById.get('login-lang').textContent.includes('English'), 'login-lang button shows English');

// Switch to Chinese
setLang('zh');
check(currentLang() === 'zh', 'currentLang() updated to zh');
check(storage.getItem('helper.chat.lang') === 'zh', 'localStorage holds zh');
check(t('send') === '发送', 'zh: t("send") is 发送');
check(getLocale() === 'zh-CN', 'zh: locale is zh-CN');
check(sandbox.document.title.includes('消息'), 'document.title localized to Chinese');
check(elementsById.get('login-lang').textContent.includes('中文'), 'login-lang button shows Chinese');

// Test fallback on unknown language
setLang('invalid_lang');
check(currentLang() === 'ru', 'fallback to ru on unknown language code');
check(t('send') === 'Отправить', 'ru fallback t("send")');

console.log('\nALL MESSENGER I18N CHECKS PASSED!');
process.exit(0);

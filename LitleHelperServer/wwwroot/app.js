"use strict";
const $ = s => document.querySelector(s);
const escape = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let token = sessionStorage.getItem('helper-token'), me, hub, page = 'computers', settingsTab = localStorage.getItem('helper-settings-tab') || 'ad', computers = [], scripts = [], selected = new Set(), terminals = new Map(), pendingOutput = new Map(), generation = 0;
let permissionCatalog={}, rolePermissions={};
const can=key=>(me?.effectivePermissions||[]).includes(key), manage=()=>can('commands.execute'), superAdmin=()=>can('terminal.execute'), roleName=r=>({SuperAdmin:'Супер администратор',Admin:'Администратор',Operator:'Оператор',User:'Пользователь'})[r]||r;
const settingsTabs = [
  { id: 'medical-statuses', title: 'Медицинские статусы', icon: '🩺', perm: 'settings.manage' },
  { id: 'ad', title: 'Active Directory', icon: '👤', perm: 'settings.manage' },
  { id: 'telegram', title: 'Telegram', icon: '✉️', perm: 'settings.manage' },
  { id: 'glpi', title: 'GLPI HelpDesk', icon: '⚙️', perm: 'settings.manage' },
  { id: 'cartridge', title: 'Учёт картриджей', icon: '🖨️', perm: 'settings.manage' },
  { id: 'emergency', title: 'Экстренное оповещение', icon: '🚨', perm: 'settings.manage' },
  { id: 'branches', title: 'Филиалы', icon: '⌂', perm: 'settings.manage' },
  { id: 'messenger', title: 'Мессенджер', icon: '☏', perm: 'settings.manage' },
  { id: 'security', title: 'Безопасность', icon: '🛡️', perm: 'settings.manage' },
  { id: 'updates', title: 'Обновления клиента', icon: '↻', perm: 'updates.manage' }
];
const titles = { 'ai-chat':['ИИ-помощник','Разговоры, база знаний и разрешённая диагностика.'], reports:["Статистика GLPI","Заявки, время исполнения и оценки специалистов."], surveys:["Оценить результат","Обратная связь по вашим закрытым заявкам."],
  messenger:["Мессенджер","Личная переписка сотрудников и хранение истории."], 
  branches:["Филиалы","Справочник филиалов для заявок и исполнителей."], 
  messages:['Сообщения клиентам','Отправка сообщений в облачке помощника: всем или выбранным ПК.'], 
  'super-buttons':['Кнопки супер админа','Отдельные шаблоны сообщений для привязанного помощника SuperAdmin.'], 
  updates:['Обновления клиента','Подписанные MSI и автоматическая установка новых версий.'], 
  security:['Безопасность','Защита от фишинга WebGuard и развертывание групповых политик.'],
  telegram:['Telegram','Уведомления о новых заявках в группе.'], 
  ad:['Active Directory','Доменный вход и доступ пользователей к своим заявкам.'], 
  glpi:['Настройки GLPI','Доступ к HelpDesk и проверка подключения с сервера помощника.'],
  settings:['Настройки системы','Единый центр управления интеграциями, параметрами сервисов и обновлениями.'], 
  cartridge:['Учёт картриджей','Оповещения сотрудников о поступлении заправленных картриджей.'], 
  emergency:['Экстренное оповещение','Интеграция с AudioRONGTA (ROXTON SOS) и вызов специалистов.'], 
  computers:['Компьютеры','Рабочие станции и состояние помощников в реальном времени.'], 
  buttons:['Кнопки помощника','Настройте действия и отправьте изменения на рабочие места.'], 
  users:['Пользователи','Учётные записи, роли и доступ к панели.'], 
  audit:['Журнал аудита','Отправленные команды, ответы и ошибки выполнения.'], 
  tickets:['Заявки GLPI','Обращения пользователей и их состояние в HelpDesk.'],
  knowledge:['База знаний','Инструкции, частые вопросы и справочник для персонала.']
};
const date = v => v ? new Date(v).toLocaleString('ru-RU') : '—';
function toast(text, error=false){ $('#toast').textContent=text; $('#toast').className=error?'error':''; $('#toast').hidden=false; clearTimeout(toast.timer); toast.timer=setTimeout(()=>$('#toast').hidden=true,6000); }
async function api(path, method='GET', body){ const response=await fetch('/api'+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body===undefined?undefined:JSON.stringify(body)}); const result=response.status===204?null:await response.json().catch(()=>null); if(!response.ok){ if(response.status===401 && !['/auth/login','/auth/ad'].includes(path)) logout(); throw new Error(result?.error || (response.status===403?'Недостаточно прав':response.status===429?'Слишком много попыток. Подождите минуту.':'Ошибка '+response.status)); } return result; }
function modal(title,html){ $('#modal-title').textContent=title; $('#modal-content').innerHTML=html; if(!$('#modal').open)$('#modal').showModal(); }
function closeModal(){ $('#modal').close(); }
$('#modal-close').addEventListener('click',()=>{ if(!me?.mustChangePassword)closeModal(); });
$('#modal').addEventListener('cancel',e=>{ if(me?.mustChangePassword)e.preventDefault(); });
$('#logout-button').addEventListener('click',logout);
$('#password-button').addEventListener('click',()=>passwordForm(false));
$('#refresh-button').addEventListener('click',()=>loadPage().catch(e=>toast(e.message,true)));
let robotTimers=[];
function resetRobot(){ const face=document.querySelector('.pixel-face'),deco=document.querySelector('.auth-decoration'); robotTimers.forEach(clearTimeout); robotTimers=[]; if(face)face.classList.remove('is-error','is-happy','is-lockout'); if(deco)deco.classList.remove('is-cracked'); }
function playRobot(state){ const face=document.querySelector('.pixel-face'),deco=document.querySelector('.auth-decoration'); if(!face)return; robotTimers.forEach(clearTimeout); robotTimers=[]; face.classList.remove('is-error','is-happy','is-lockout'); if(deco)deco.classList.remove('is-cracked'); void face.offsetWidth; if(state==='error'){ face.classList.add('is-error'); robotTimers.push(setTimeout(()=>face.classList.remove('is-error'),1400)); } else if(state==='happy'){ face.classList.add('is-happy'); robotTimers.push(setTimeout(()=>face.classList.remove('is-happy'),1500)); } else if(state==='lockout'){ face.classList.add('is-lockout'); if(deco)deco.classList.add('is-cracked'); robotTimers.push(setTimeout(()=>{ face.classList.remove('is-lockout'); if(deco)deco.classList.remove('is-cracked'); resetFailedLogins(); },4000)); } }
let failedLogins=Number(sessionStorage.getItem('helper-failed-logins')||0);
function resetFailedLogins(){ failedLogins=0; sessionStorage.removeItem('helper-failed-logins'); }
$('#login-form').addEventListener('submit',async e=>{ e.preventDefault(); const button=e.target.querySelector('button'); button.disabled=true; $('#login-error').textContent=''; resetRobot(); try{ const data=Object.fromEntries(new FormData(e.target)); const login=await api(data.provider==='ad'?'/auth/ad':'/auth/login','POST',data); resetFailedLogins(); playRobot('happy'); acceptLogin(login); await new Promise(resolve=>setTimeout(resolve,900)); await enter(); }catch(error){ if(token){ toast(error.message,true); }else{ $('#login-error').textContent=error.message; failedLogins+=1; sessionStorage.setItem('helper-failed-logins',failedLogins); playRobot(failedLogins>5?'lockout':'error'); } }finally{button.disabled=false;} });
function acceptLogin(login){ token=login.token; me=login.user; sessionStorage.setItem('helper-token',token); }
function logout(){ token=null; me=null; generation++; sessionStorage.removeItem('helper-token'); if(hub)hub.stop(); hub=null; $('#workspace').hidden=true; $('#login-screen').hidden=false; $('#login-error').textContent=''; resetRobot(); closeModal(); }
async function enter(){ me=await api('/auth/me'); $('#login-screen').hidden=true; $('#workspace').hidden=false; $('#password-button').hidden=me.authSource==='AD'; $('#identity').innerHTML=`${escape(me.fullName||me.username)}<small>${escape(roleName(me.role))}</small>`; const nav=['computers','buttons','super-buttons','messages','tickets','reports','surveys','knowledge','users','audit','settings'].filter(n=>n==='tickets'||n==='surveys'||(n==='ai-chat'&&can('ai.chat'))||(n==='reports'&&can('reports.glpi.view'))||n==='knowledge'||(['messages','super-buttons'].includes(n)&&me.role==='SuperAdmin')||(n==='settings'&&(can('settings.manage')||can('updates.manage')||can('ai.settings.manage')))||can(({computers:'computers.view',buttons:'buttons.manage',users:'users.manage',audit:'audit.view'})[n])); $('#navigation').innerHTML=nav.map(n=>`<button data-page="${n}">${({computers:'▦',buttons:'◇',messages:'✉','super-buttons':'★',users:'♙',audit:'≡',tickets:'▤',reports:'▥',surveys:'☆',knowledge:'📚','ai-chat':'✦',settings:'⚙'})[n]} &nbsp; ${titles[n][0]}</button>`).join(''); $('#navigation').querySelectorAll('button').forEach(b=>b.addEventListener('click',()=>{page=b.dataset.page;loadPage().catch(e=>toast(e.message,true));})); if(me.mustChangePassword){passwordForm(true);return;} if(new URLSearchParams(location.search).get('returnTo')==='/guard-white.html' && can('emergency.manage')){location.replace('/guard-white.html');return;} const requestedPage=new URLSearchParams(location.search).get('page');if(requestedPage&&nav.includes(requestedPage))page=requestedPage;page=nav.includes(page)?page:nav[0]; if(manage())scripts=await api('/scripts'); await connectHub(); startSurveyAdminAlerts(); await loadPage(); }
function passwordForm(required){ modal(required?'Смените начальный пароль':'Смена пароля',`<form id="password-form"><p>Новый пароль: от 8 до 72 символов, до 72 байт UTF-8.</p><label>Текущий пароль<input name="currentPassword" type="password" autocomplete="current-password" required></label><label>Новый пароль<input name="newPassword" type="password" autocomplete="new-password" minlength="8" maxlength="72" required></label><div class="error" id="form-error"></div><button class="primary">Сохранить пароль</button></form>`); $('#password-form').addEventListener('submit',async e=>{ e.preventDefault(); try{ const login=await api('/auth/change-password','POST',Object.fromEntries(new FormData(e.target))); acceptLogin(login); if(hub)await hub.stop(); hub=null; closeModal(); await enter(); toast('Пароль изменён'); }catch(error){$('#form-error').textContent=error.message;} }); }
async function connectHub(){ if(hub)return; if(!window.signalR){$('#live-status').textContent='● Библиотека SignalR недоступна';return;} hub=new signalR.HubConnectionBuilder().withUrl('/helperHub',{accessTokenFactory:()=>token}).withAutomaticReconnect().build(); const live=online=>{$('#live-status').textContent=online?'● Обновления подключены':'● Соединение потеряно';$('#live-status').className='connection'+(online?' online':'');}; hub.on('ComputerChanged',computer=>{const index=computers.findIndex(c=>c.id===computer.id);if(index<0)computers.push(computer);else computers[index]=computer; if(page==='computers')renderComputers();}); hub.on('ExecutionOutput',e=>{ const task=terminals.get(e.taskId);if(task){task.output+=e.output;renderTerminal();}else{const cached=pendingOutput.get(e.taskId)||{output:''};cached.output=(cached.output+e.output).slice(0,131072);pendingOutput.set(e.taskId,cached);if(pendingOutput.size>256)pendingOutput.delete(pendingOutput.keys().next().value);} }); hub.on('ExecutionResult',e=>{ const task=terminals.get(e.taskId);if(task){task.output=e.result;task.status=e.status;task.exitCode=e.exitCode;renderTerminal();}else{pendingOutput.set(e.taskId,{...e,output:e.result});if(pendingOutput.size>256)pendingOutput.delete(pendingOutput.keys().next().value);} if(page==='audit')loadPage().catch(()=>{}); }); hub.on('TicketCreated',()=>{if(page==='tickets')loadPage().catch(()=>{});}); hub.on('SurveyAttentionChanged',()=>pollSurveyAdminAlerts().catch(()=>{})); hub.onreconnecting(()=>live(false)); hub.onreconnected(()=>live(true)); hub.onclose(()=>live(false)); try{await hub.start();live(true);}catch{live(false);toast('Нет живого соединения. Таблицы можно обновлять вручную.',true);} }
async function loadPage(){ if(!me||me.mustChangePassword)return; const revision=++generation; if(page==='ai-chat'){$('#page-title').textContent=titles[page][0];$('#page-subtitle').textContent=titles[page][1];$('#stats').hidden=true;$('#navigation').querySelectorAll('button').forEach(b=>b.classList.toggle('active',b.dataset.page===page));await renderAiChatPage();return;} if(page==='reports'||page==='surveys') { $('#page-title').textContent=titles[page][0]; $('#page-subtitle').textContent=titles[page][1]; $('#navigation').querySelectorAll('button').forEach(b=>b.classList.toggle('active',b.dataset.page===page)); if(page==='reports')await renderGlpiReports();else await renderTicketSurveys();return; } if(['ad','telegram','glpi','cartridge','emergency','branches','messenger','security','updates'].includes(page)){settingsTab=page;page='settings';} $('#page-title').textContent=titles[page][0]; $('#page-subtitle').textContent=titles[page][1]; $('#navigation').querySelectorAll('button').forEach(b=>b.classList.toggle('active',b.dataset.page===page)); $('#content').innerHTML='<div class="empty">Загрузка…</div>'; if(page==='settings'){const available=settingsTabs.filter(t=>can(t.perm));if(!available.length){$('#content').innerHTML='<div class="empty">Нет доступных разделов настроек</div>';return;}if(!available.some(t=>t.id===settingsTab))settingsTab=available[0].id;localStorage.setItem('helper-settings-tab',settingsTab);const curTab=available.find(t=>t.id===settingsTab);$('#page-title').textContent='Настройки · '+(curTab?.title||'Система');$('#page-subtitle').textContent=titles[settingsTab] ? titles[settingsTab][1] : titles.settings[1];$('#content').innerHTML=`<div class="settings-tabs">${available.map(t=>`<button type="button" class="settings-tab-btn ${t.id===settingsTab?'active':''}" data-settings-tab="${t.id}"><span class="tab-icon">${t.icon}</span> <span>${escape(t.title)}</span></button>`).join('')}</div><div id="settings-tab-body"><div class="empty">Загрузка…</div></div>`;$('#content').querySelectorAll('[data-settings-tab]').forEach(btn=>btn.addEventListener('click',()=>{settingsTab=btn.dataset.settingsTab;localStorage.setItem('helper-settings-tab',settingsTab);loadPage().catch(e=>toast(e.message,true));}));$('#stats').hidden=true;const rev=generation;try{if(settingsTab==='ai'){await renderAiSettings();}else if(settingsTab==='medical-statuses'){const data=await api('/settings/medical-statuses');if(rev!==generation||!$('#settings-tab-body'))return;renderMedicalStatuses(data);return;}if(settingsTab==='ad'||settingsTab==='telegram'){const data=await api('/settings/'+settingsTab);if(rev!==generation||!$('#settings-tab-body'))return;if(settingsTab==='ad')renderAdSettings(data);else renderTelegramSettings(data);return;}if(settingsTab==='glpi'){const data=await api('/settings/glpi');if(rev!==generation||!$('#settings-tab-body'))return;renderGlpiSettings(data);return;}if(settingsTab==='cartridge'){const data=await api('/settings/cartridge');if(rev!==generation||!$('#settings-tab-body'))return;renderCartridgeSettings(data);return;}if(settingsTab==='emergency'){const [data,branches,recipients]=await Promise.all([api('/settings/emergency'),api('/branches'),api('/settings/emergency/recipients')]);if(rev!==generation||!$('#settings-tab-body'))return;renderEmergencySettings(data,branches,recipients);return;}if(settingsTab==='branches'){const data=await api('/branches');if(rev!==generation||!$('#settings-tab-body'))return;renderBranches(data);return;}if(settingsTab==='messenger'){const data=await api('/settings/messenger');if(rev!==generation||!$('#settings-tab-body'))return;renderMessengerSettings(data);return;}if(settingsTab==='security'){const data=await api('/settings/security');if(rev!==generation||!$('#settings-tab-body'))return;renderSecuritySettings(data);return;}if(settingsTab==='updates'){const data=await api('/settings/updates');if(rev!==generation||!$('#settings-tab-body'))return;renderClientUpdates(data);return;}}catch(err){if($('#settings-tab-body'))$('#settings-tab-body').innerHTML=`<div class="error" style="padding:20px">${escape(err.message)}</div>`;}return;} if(page==='computers'){computers=await api('/computers');if(revision!==generation)return;$('#content').innerHTML=`<div class="toolbar"><input id="computer-filter" placeholder="Поиск по имени ПК, домену, кабинету или пользователю"><select id="status-filter"><option value="all">Все статусы</option><option value="online">Онлайн</option><option value="offline">Офлайн</option></select>${can('computers.manage')?'<button id="enroll-button" class="primary">＋ Подключить ПК</button>':''}</div>${manage()?`<div class="bulk"><span id="selection-count">Выбрано: 0</span><select id="bulk-type">${superAdmin()?'<option value="cmd">CMD</option><option value="powershell">PowerShell</option>':''}<option value="script">Предустановленный скрипт</option><option value="reboot">Перезагрузка</option><option value="shutdown">Выключение</option><option value="inventory">Обновить инвентарь</option></select><input id="bulk-payload" placeholder="Введите команду"><select id="bulk-script" hidden>${scripts.map(s=>`<option value="${escape(s.id)}">${escape(s.title)}</option>`).join('')}</select><button id="bulk-send" class="primary">Для выбранных</button><button id="bulk-all">Для всех ПК</button></div>`:''}<div class="panel"><div class="panel-head"><h2>Рабочие станции</h2><span id="computer-count"></span></div><div class="table-wrap"><table><thead><tr>${manage()?'<th><input id="select-visible" type="checkbox" aria-label="Выбрать видимые ПК"></th>':''}<th>КОМПЬЮТЕР</th><th>ПОЛЬЗОВАТЕЛЬ</th><th>КАБИНЕТ</th><th>СТАТУС</th><th>ПОСЛЕДНЯЯ СВЯЗЬ</th>${computerActions()?'<th>ДЕЙСТВИЯ</th>':''}</tr></thead><tbody id="computer-rows"></tbody></table></div></div>`; $('#computer-filter').addEventListener('input',renderComputers);$('#status-filter').addEventListener('change',renderComputers); if(can('computers.manage'))$('#enroll-button').addEventListener('click',enrollForm); if(manage()){ $('#select-visible').addEventListener('change',e=>{filteredComputers().forEach(c=>e.target.checked?selected.add(c.machineName):selected.delete(c.machineName));renderComputers();}); $('#bulk-type').addEventListener('change',bulkFields); $('#bulk-send').addEventListener('click',()=>bulkSend(false));$('#bulk-all').addEventListener('click',()=>bulkSend(true));bulkFields(); }renderComputers();return;} if(page==='messages'){const [pcs,buttons]=await Promise.all([api('/computers'),api('/super-buttons')]);if(revision!==generation)return;$('#stats').hidden=true;renderMessages(pcs,buttons);return;} if(page==='super-buttons'){const buttons=await api('/super-buttons');if(revision!==generation)return;$('#stats').hidden=true;renderSuperButtons(buttons);return;} if(page==='knowledge'){const articles=await api('/knowledge-base');if(revision!==generation)return;$('#stats').hidden=true;renderKnowledgeBase(articles);return;} const items=await api('/'+page+(page==='tickets'?'?status='+encodeURIComponent(ticketStatusFilter)+(ticketBranchFilter?'&branchId='+encodeURIComponent(ticketBranchFilter):''):'')); if(revision!==generation)return; $('#stats').hidden=true; if(page==='buttons')renderButtons(items);if(page==='users'){const definitions=await api('/permissions');if(revision!==generation)return;permissionCatalog=definitions.catalog;rolePermissions=definitions.roles;renderUsers(items,await api("/branches"));}if(page==='audit')renderAudit(items);if(page==='tickets'){const branches=await api('/branches');if(revision!==generation)return;renderTickets(items,branches);} }

function filteredComputers(){ const q=$('#computer-filter')?.value.toLowerCase()||'',status=$('#status-filter')?.value||'all';return computers.filter(c=>(c.machineName+' '+c.currentUser+' '+(c.room||'')+' '+c.domainName).toLowerCase().includes(q)&&(status==='all'||c.isOnline===(status==='online'))); }
const computerActions=()=>manage()||can('inventory.view')||can('computers.delete');
function renderComputers(){if(!$('#computer-rows'))return;const rows=filteredComputers();$('#stats').hidden=false;$('#stats').innerHTML=[['Всего компьютеров',computers.length,''],['Сейчас онлайн',computers.filter(c=>c.isOnline).length,'green'],['Не в сети',computers.filter(c=>!c.isOnline).length,''],['Выбрано',selected.size,'blue']].map(s=>`<div class="stat"><span>${s[0]}</span><strong class="${s[2]}">${s[1]}</strong></div>`).join('');$('#computer-count').textContent=rows.length+' показано';$('#computer-rows').innerHTML=rows.map(c=>`<tr>${manage()?`<td><input type="checkbox" data-select="${escape(c.machineName)}" ${selected.has(c.machineName)?'checked':''} aria-label="Выбрать ${escape(c.machineName)}"></td>`:''}<td><strong>${escape(c.machineName)}</strong><small>${escape(c.domainName||'Без домена')}</small></td><td>${escape(c.currentUser||'—')}</td><td><strong>${escape(c.room||'—')}</strong>${can('computers.manage')?` <button class="quiet" data-edit-computer="${c.id}" title="Изменить кабинет и филиал">✏</button>`:''}</td><td><span class="status${c.isOnline?'':' off'}">● ${c.isOnline?'Online':'Offline'}</span></td><td><small>${date(c.lastSeen)}</small></td>${computerActions()?`<td><div class="actions">${can('computers.manage')?`<button data-action="edit" data-id="${c.id}">✏ Изменить</button>`:''}${manage()&&can('terminal.execute')?`<button data-action="terminal" data-id="${c.id}">⚡ Терминал</button>`:''}${can('inventory.view')?`<button data-action="inventory" data-id="${c.id}">⌕ Инвентарь</button>`:''}${can('computers.delete')?`<button data-action="delete" data-id="${c.id}">Удалить</button>`:''}${manage()?`<button data-action="power" data-id="${c.id}">↻ Питание</button><button data-action="kill" data-id="${c.id}">× Процесс</button><button data-action="script" data-id="${c.id}">▷ Скрипт</button>`:''}</div></td>`:''}</tr>`).join('')||`<tr><td colspan="7"><div class="empty"><strong>Пока нет рабочих станций</strong>Подключите ПК и укажите выданный ключ в конфигурации помощника.</div></td></tr>`;$('#computer-rows').querySelectorAll('[data-select]').forEach(input=>input.addEventListener('change',()=>{input.checked?selected.add(input.dataset.select):selected.delete(input.dataset.select);renderComputers();}));$('#computer-rows').querySelectorAll('[data-action]').forEach(button=>button.addEventListener('click',()=>computerAction(button.dataset.action,computers.find(c=>c.id===Number(button.dataset.id))).catch(e=>toast(e.message,true))));$('#computer-rows').querySelectorAll('[data-edit-computer]').forEach(button=>button.addEventListener('click',()=>editComputerForm(computers.find(c=>c.id===Number(button.dataset.editComputer))).catch(e=>toast(e.message,true))));if($('#selection-count'))$('#selection-count').textContent='Выбрано: '+selected.size; }
function bulkFields(){const type=$('#bulk-type').value;$('#bulk-script').hidden=type!=='script';$('#bulk-payload').hidden=!['cmd','powershell','kill'].includes(type);}
async function bulkSend(all){const machines=all?['All']:[...selected];if(!machines.length){toast('Выберите компьютеры',true);return;}const type=$('#bulk-type').value,payload=type==='script'?$('#bulk-script').value:$('#bulk-payload').value;if(!confirm(`Отправить «${type}» ${all?'на все зарегистрированные ПК':`на ${machines.length} ПК`}?`))return;try{const tasks=await sendCommand(machines,type,payload);toast('Создано задач: '+tasks.length);if(['cmd','powershell','script'].includes(type))terminalForm(machines,tasks);}catch(e){toast(e.message,true);}}
async function sendCommand(machines,type,payload){const tasks=await api('/commands','POST',{machines,type,payload:payload||''});for(const t of tasks)terminals.set(t.taskId,{...t,output:t.result||'',...(pendingOutput.get(t.taskId)||{})});return tasks;}
async function computerAction(action,c){if(action==='edit')return editComputerForm(c);if(action==='terminal')return terminalForm([c.machineName]);if(action==='inventory')return inventoryForm(c);if(action==='delete'){if(confirm('Удалить регистрацию '+c.machineName+'?')){await api('/computers/'+c.id,'DELETE');await loadPage();}return;}if(action==='power'){modal('Питание · '+c.machineName,'<p>Операция произойдёт через 5 секунд с принудительным закрытием приложений. Несохранённые данные могут быть потеряны.</p><div class="form-actions"><button id="reboot" class="secondary">Перезагрузить</button><button id="shutdown" class="danger">Выключить</button></div>');for(const type of ['reboot','shutdown'])$('#'+type).addEventListener('click',async()=>{if(!confirm('Подтвердить '+type+' для '+c.machineName+'?'))return;try{await sendCommand([c.machineName],type,'');closeModal();toast('Команда отправлена');}catch(e){toast(e.message,true);}});return;}if(action==='kill')return processForm(c);if(action==='script'){modal('Отправить скрипт · '+c.machineName,`<form id="command-form"><label>Предустановленный скрипт<select name="payload">${scripts.map(s=>`<option value="${escape(s.id)}">${escape(s.title)}</option>`).join('')}</select></label><button class="primary">Отправить</button></form>`);$('#command-form').addEventListener('submit',async e=>{e.preventDefault();try{const tasks=await sendCommand([c.machineName],'script',new FormData(e.target).get('payload'));terminalForm([c.machineName],tasks);}catch(error){toast(error.message,true);}});}}
async function editComputerForm(c){const branches=await api('/branches');modal('Редактирование рабочей станции · '+c.machineName,`<form id="edit-computer-form"><p><strong>${escape(c.machineName)}</strong> · ${escape(c.currentUser||'Без пользователя')} (${c.isOnline?'Онлайн':'Офлайн'})</p><label>Филиал<select name="branchId"><option value="">Не назначен (по префиксу ПК)</option>${branches.map(b=>`<option value="${b.id}" ${c.branchId===b.id?'selected':''}>${escape(b.name)}${b.isActive?'':' (отключён)'}</option>`).join('')}</select></label><label>Кабинет<input name="room" maxlength="100" value="${escape(c.room||'')}" placeholder="Например: 120"></label><div class="error" id="form-error"></div><div class="form-actions"><button class="primary">Сохранить</button></div></form>`);$('#edit-computer-form').addEventListener('submit',async e=>{e.preventDefault();const branchVal=e.target.elements.branchId.value;const roomVal=e.target.elements.room.value.trim();try{await api('/computers/'+c.id,'PUT',{room:roomVal,branchId:branchVal?Number(branchVal):null});c.room=roomVal;c.branchId=branchVal?Number(branchVal):null;closeModal();toast('Данные ПК сохранены');renderComputers();}catch(err){$('#form-error').textContent=err.message;}});}
function terminalForm(machines,tasks=[]){modal('Консоль · '+machines.join(', '),`<div id="terminal-output" class="terminal">Ожидание команды…</div>${superAdmin()?'<form id="terminal-form" class="terminal-input"><select name="type"><option value="cmd">CMD</option><option value="powershell">PowerShell</option></select><input name="payload" placeholder="Введите команду" maxlength="8000" autocomplete="off" required><button class="primary">Выполнить</button></form>':''}<div class="task-label"><span>Ограничение: 3 минуты, 128 КиБ вывода</span><span>Права текущего пользователя ПК</span></div>`);$('#terminal-output').dataset.machines=JSON.stringify(machines);renderTerminal();if($('#terminal-form'))$('#terminal-form').addEventListener('submit',async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));try{await sendCommand(machines,data.type,data.payload);e.target.payload.value='';renderTerminal();}catch(error){toast(error.message,true);}});}
function renderTerminal(){const output=$('#terminal-output');if(!output)return;const machines=JSON.parse(output.dataset.machines);output.textContent=[...terminals.values()].filter(t=>machines.includes(t.machineName)).map(t=>`[${t.machineName}] ${t.commandType} · ${t.status}${t.exitCode==null?'':' · exit '+t.exitCode}\n${t.output}`).join('\n\n')||'Ожидание команды…';output.scrollTop=output.scrollHeight;}
function enrollForm(){modal('Подключить рабочую станцию','<form id="enroll-form"><p>Введите имя ПК. Для уже зарегистрированного отключённого ПК будет выдан новый ключ.</p><label>Имя компьютера<input name="machineName" required maxlength="63" placeholder="PC-001"></label><button class="primary">Выдать ключ</button></form>');$('#enroll-form').addEventListener('submit',async e=>{e.preventDefault();try{const result=await api('/computers/enroll','POST',Object.fromEntries(new FormData(e.target)));modal('Ключ · '+result.machineName,`<p>Сохраните ключ: он показывается только сейчас. Укажите его в <code>clientToken</code> файла config.json на этом ПК.</p><div class="key">${escape(result.clientToken)}</div><p><small>Для связи вне доверенной локальной сети используйте HTTPS.</small></p>`);await loadPage();}catch(error){toast(error.message,true);}});}
async function inventoryForm(c){const inventory=await api('/computers/'+c.id+'/inventory'),h=inventory.hardware;modal('Инвентарь · '+c.machineName,`<div class="inventory-grid"><div><small>Процессор</small>${escape(h.cpuModel||'Нет данных')}</div><div><small>RAM</small>${h.totalRamBytes?(h.totalRamBytes/1073741824).toFixed(1)+' ГиБ':'Нет данных'}</div><div><small>Windows</small>${escape(inventory.osVersion)}</div><div><small>IP</small>${escape(inventory.ipAddress)}</div></div><button id="inventory-refresh">↻ Запросить новый инвентарь</button><h3 class="section-title">Планки памяти</h3>${(h.memorySlots||[]).map(r=>`<p>${escape(r.slot)} · ${(r.capacityBytes/1073741824).toFixed(1)} ГиБ · ${escape(r.manufacturer)} · ${r.speedMHz} МГц</p>`).join('')||'<small>Нет данных</small>'}<h3 class="section-title">Физические диски</h3>${(h.physicalDisks||[]).map(d=>`<p>${escape(d.model)} · ${(d.sizeBytes/1073741824).toFixed(1)} ГиБ · ${escape(d.interfaceType)}</p>`).join('')}<h3 class="section-title">Логические диски</h3>${(h.logicalDisks||[]).map(d=>`<p>${escape(d.name)} · свободно ${(d.freeBytes/1073741824).toFixed(1)} / ${(d.totalBytes/1073741824).toFixed(1)} ГиБ</p>`).join('')}<h3 class="section-title">Принтеры (${(h.printers||[]).length})</h3>${(h.printers||[]).map(p=>`<p>🖨️ <strong>${escape(p.name)}</strong> ${p.isDefault?'<span class="pill">По умолчанию</span>':''} · <span class="${p.hasError?'error':'pill'}">${escape(p.statusText)}</span>${p.jobCount>0?' <em>(заданий: '+p.jobCount+')</em>':''}</p>`).join('')||'<small>Принтеры не обнаружены</small>'}<h3 class="section-title">Установленные программы (${inventory.software.length})</h3><input id="software-filter" placeholder="Найти программу"><div id="software-list"></div>${h.errors?.length?`<p class="error">${escape(h.errors.join('; '))}</p>`:''}`);const render=()=>{$('#software-list').innerHTML=inventory.software.filter(s=>s.name.toLowerCase().includes($('#software-filter').value.toLowerCase())).map(s=>`<p>${escape(s.name)} <small>${escape(s.version)}</small></p>`).join('');};$('#software-filter').addEventListener('input',render);render();$('#inventory-refresh').addEventListener('click',async()=>{try{await sendCommand([c.machineName],'inventory','');toast('Инвентаризация запрошена. Откройте карточку повторно после ответа.');}catch(e){toast(e.message,true);}});}
function table(headers,rows){return `<div class="panel"><div class="table-wrap"><table><thead><tr>${headers.map(h=>`<th>${h}</th>`).join('')}</tr></thead><tbody>${rows||`<tr><td colspan="${headers.length}"><div class="empty">Пока нет записей</div></td></tr>`}</tbody></table></div></div>`;}
function renderButtons(items){$('#content').innerHTML=`<div class="toolbar"><button id="add-button" class="primary">＋ Добавить кнопку</button><button id="apply-buttons">Применить изменения</button><small>Сохранение меняет БД. Применение отправляет меню подключённым ПК.</small></div>`+table(['НАЗВАНИЕ','ТИП / ЗНАЧЕНИЕ','ГРУППА','ПОРЯДОК','СТАТУС','ДЕЙСТВИЯ'],items.map(b=>`<tr><td><strong>${escape(b.title)}</strong>${b.isSystem?'<span class="pill">🔒 Системная</span>':''}<small>${escape(b.iconName)}</small></td><td class="wrap">${escape(b.actionType)}<small>${escape(b.payload)}</small></td><td>${escape(b.targetGroup)}</td><td>${b.orderIndex}</td><td><span class="pill ${b.isActive?'':'dim'}">${b.isActive?'Активна':'Выключена'}</span></td><td><div class="actions"><button data-toggle="${b.id}" class="${b.isActive?'':'primary'}">${b.isActive?'Отключить':'Включить'}</button>${b.isSystem?'':'<button data-edit="'+b.id+'">Изменить</button><button data-delete="'+b.id+'">Удалить</button>'}</div></td></tr>`).join(''));$('#add-button').addEventListener('click',()=>buttonForm());$('#apply-buttons').addEventListener('click',async()=>{try{const result=await api('/buttons/apply','POST',{});toast('Обновлено помощников: '+result.notified);}catch(e){toast(e.message,true);}});$('#content').querySelectorAll('[data-toggle]').forEach(b=>b.addEventListener('click',async()=>{try{const res=await api('/buttons/'+b.dataset.toggle+'/toggle','POST',{});toast(res.isActive?'Кнопка включена и отправлена на ПК':'Кнопка отключена и скрыта на ПК');await loadPage();}catch(e){toast(e.message,true);}}));$('#content').querySelectorAll('[data-edit]').forEach(b=>b.addEventListener('click',()=>buttonForm(items.find(i=>i.id===Number(b.dataset.edit)))));$('#content').querySelectorAll('[data-delete]').forEach(b=>b.addEventListener('click',()=>remove('/buttons/'+b.dataset.delete,'Удалить кнопку?')));}
function buttonForm(button={title:'',actionType:'open_url',payload:'',orderIndex:0,isActive:true,targetGroup:'All',iconName:''}){modal(button.id?'Изменить кнопку':'Новая кнопка',`<form id="button-form"><label>Название<input name="title" value="${escape(button.title)}" maxlength="100" required></label><div class="form-row"><label>Тип<select name="actionType">${['open_folder','open_url',...(superAdmin()?['run_command']:[]),'ticket'].map(type=>`<option ${button.actionType===type?'selected':''}>${type}</option>`).join('')}</select></label><label>Порядок<input name="orderIndex" type="number" min="0" max="10000" value="${button.orderIndex}" required></label></div><label>Значение / путь<textarea name="payload" maxlength="8000">${escape(button.payload)}</textarea></label><small>run_command: полный путь EXE либо JSON {"executable":"C:\\...exe","arguments":[]}.</small><div class="form-row"><label>Группа<input name="targetGroup" value="${escape(button.targetGroup)}" maxlength="120" required placeholder="All / domain:DOMAIN / pc:PCNAME"></label><label>Имя значка<input name="iconName" value="${escape(button.iconName)}" maxlength="50"></label></div><label class="check-label"><input name="isActive" type="checkbox" ${button.isActive?'checked':''}>Активна</label><div class="error" id="form-error"></div><button class="primary">Сохранить</button></form>`);$('#button-form').addEventListener('submit',async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));data.orderIndex=Number(data.orderIndex);data.isActive=e.target.isActive.checked;try{await api('/buttons'+(button.id?'/'+button.id:''),button.id?'PUT':'POST',data);closeModal();await loadPage();}catch(error){$('#form-error').textContent=error.message;}});}
function userBranchLabels(u,branches=[]){if(u.role==='SuperAdmin')return'<span class="pill">🌐 Все филиалы</span>';const ids=Array.isArray(u.branchIds)&&u.branchIds.length?u.branchIds:(u.branchId?[u.branchId]:[]);if(!ids.length)return u.role==='Admin'?'<span class="pill">🌐 Все филиалы</span>':'Не назначен';const names=ids.map(id=>branches.find(b=>b.id===id)?.name).filter(Boolean);if(!names.length)return'Не назначен';if(names.length===1)return escape(names[0]);return`<span title="${escape(names.join(', '))}" style="font-weight:500">${escape(names.join(', '))}</span>`;}
function renderUsers(items,branches=[]){$('#content').innerHTML='<div class="toolbar"><input id="user-filter" placeholder="Поиск по имени, логину, кабинету или роли"><button id="add-user" class="primary">＋ Создать пользователя</button><small>Локальные и AD-пользователи Control Center. AD-пользователь появляется после первого входа.</small></div>'+table(['ПОЛЬЗОВАТЕЛЬ','РОЛЬ','ФИЛИАЛ','КАБИНЕТ','СТАТУС','СОЗДАН','ДЕЙСТВИЯ'],items.map(u=>`<tr><td><strong>${escape(u.fullName)}</strong><small>${escape(u.username)} · ${escape(u.authSource||'Local')}</small></td><td><span class="pill">${escape(roleName(u.role))}</span></td><td>${userBranchLabels(u,branches)}</td><td><strong>${escape(u.room||'—')}</strong></td><td>${u.isActive?'Активен':'Отключён'}${u.mustChangePassword?'<small>Смена пароля обязательна</small>':''}</td><td>${date(u.createdAt)}</td><td><div class="actions"><button data-edit="${u.id}">Изменить / пароль</button>${u.authSource==='AD'?'':`<button data-delete="${u.id}">Удалить</button>`}</div></td></tr>`).join(''));$('#user-filter').addEventListener('input',e=>{$('#content').querySelectorAll('tbody tr').forEach(row=>row.hidden=!row.textContent.toLowerCase().includes(e.target.value.toLowerCase()));});$('#add-user').addEventListener('click',()=>userForm());$('#content').querySelectorAll('[data-edit]').forEach(b=>b.addEventListener('click',()=>userForm(items.find(i=>i.id===Number(b.dataset.edit)))));$('#content').querySelectorAll('[data-delete]').forEach(b=>b.addEventListener('click',()=>remove('/users/'+b.dataset.delete,'Удалить пользователя?')));}
function renderUserPermissions(user,preserve=false){
 const container=$('#user-permissions'),form=$('#user-form'); if(!container)return;
 const overrides=preserve?Object.fromEntries([...container.querySelectorAll('[data-permission]')].filter(x=>x.value!=='inherit').map(x=>[x.dataset.permission,x.value==='allow'])):(user.permissions||{}),role=form.elements.role.value;
 container.innerHTML=Object.entries(permissionCatalog).map(([key,label])=>`<label>${escape(label)}<select data-permission="${escape(key)}" ${role==='SuperAdmin'?'disabled':''}><option value="inherit" ${overrides[key]===undefined?'selected':''}>По роли: ${(rolePermissions[role]||[]).includes(key)?'разрешено':'запрещено'}</option><option value="allow" ${overrides[key]===true?'selected':''}>Разрешить</option><option value="deny" ${overrides[key]===false?'selected':''}>Запретить</option></select></label>`).join('');
}
async function userForm(user={username:'',fullName:'',role:'User',isActive:true,room:''}){let bindingMachines=[];const userBranches=await api('/branches');if(me.role==='SuperAdmin'){try{bindingMachines=await api('/computers');}catch(e){toast(e.message,true);return;}}const assignedBranchIds=Array.isArray(user.branchIds)&&user.branchIds.length?user.branchIds:(user.branchId?[user.branchId]:[]);modal(user.id?'Учётная запись':'Новый пользователь',`<form id="user-form"><label>Логин<input name="username" value="${escape(user.username)}" maxlength="100" ${user.authSource==='AD'?'readonly':''} required></label><label>Полное имя<input name="fullName" value="${escape(user.fullName)}" maxlength="150"></label><div class="form-row"><label>Роль<select name="role">${['SuperAdmin','Admin','Operator','User'].map(role=>`<option value="${role}" ${user.role===role?'selected':''}>${roleName(role)}</option>`).join('')}</select></label><label>Пароль ${user.id?'(оставьте пустым для сохранения)':''}<input type="password" name="password" autocomplete="new-password" minlength="8" maxlength="72" ${user.authSource==='AD'?'disabled':''} ${user.id?'':'required'}></label></div>${me.role==='SuperAdmin'?`<label id="assistant-binding" ${user.role!=='SuperAdmin'?'hidden':''}>Помощник супер администратора<select name="assistantMachine"><option value="">Не привязан</option>${bindingMachines.map(c=>`<option value="${escape(c.machineName)}" ${user.assistantMachine===c.machineName?'selected':''}>${escape(c.machineName)} · ${c.isOnline?'Онлайн':'Офлайн'}</option>`).join('')}</select><small>Отправка из помощника требует входа SuperAdmin и ключа этого ПК.</small></label>`:''}<label>Кабинет<input name="room" maxlength="100" value="${escape(user.room||'')}" placeholder="Например, 120"></label><fieldset id="user-branches-fieldset" style="margin-top:0.75rem;margin-bottom:0.75rem;padding:0.75rem 1rem;border:1px solid var(--border,#CBD5E1);border-radius:8px"><legend style="font-weight:600;padding:0 0.5rem">Обслуживаемые филиалы</legend><div id="user-branches-hint" style="margin-bottom:0.5rem;font-size:0.85rem;color:var(--muted,#64748B)">${user.role==='Admin'?'Отметьте филиалы, которые обслуживает данный администратор. Если ни один филиал не отмечен — администратор имеет доступ ко всем филиалам организации.':user.role==='SuperAdmin'?'Суперадминистратор имеет доступ ко всем филиалам организации.':'Отметьте филиал (или несколько филиалов), к которым привязан сотрудник.'}</div><div class="user-branches-grid" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:0.5rem;max-height:180px;overflow-y:auto;padding:2px 0">${userBranches.map(b=>`<label class="check-label" style="display:flex;align-items:center;gap:6px;font-size:0.9rem;cursor:pointer"><input type="checkbox" name="userBranchItem" value="${b.id}" ${assignedBranchIds.includes(b.id)?'checked':''}><span>${escape(b.name)}${b.isActive?'':' <small style="color:var(--muted,#94a3b8)">(откл.)</small>'}</span></label>`).join('')}</div><div style="margin-top:0.6rem;display:flex;gap:0.5rem"><button type="button" id="btn-select-all-branches" style="font-size:0.8rem;padding:3px 10px;cursor:pointer">Выбрать все</button><button type="button" id="btn-clear-all-branches" style="font-size:0.8rem;padding:3px 10px;cursor:pointer">Снять все</button></div></fieldset><label class="check-label"><input name="isActive" type="checkbox" ${user.isActive?'checked':''}>Активен</label><small>Для AD логин и пароль управляются в домене. Для блокировки входа отключите учётную запись.</small><fieldset><legend>Отдельные права доступа</legend><small>По роли — стандартные права. Разрешить / запретить — индивидуальное исключение. SuperAdmin всегда имеет все права.</small><div id="user-permissions"></div></fieldset><div class="error" id="form-error"></div><button class="primary">Сохранить</button></form>`);renderUserPermissions(user);$('#user-form').elements.role.addEventListener('change',e=>{renderUserPermissions(user,true);if($('#assistant-binding'))$('#assistant-binding').hidden=e.target.value!=='SuperAdmin';const hint=$('#user-branches-hint');if(hint){hint.textContent=e.target.value==='Admin'?'Отметьте филиалы, которые обслуживает данный администратор. Если ни один филиал не отмечен — администратор имеет доступ ко всем филиалам организации.':e.target.value==='SuperAdmin'?'Суперадминистратор имеет доступ ко всем филиалам организации.':'Отметьте филиал (или несколько филиалов), к которым привязан сотрудник.';}});const btnAll=$('#btn-select-all-branches'),btnNone=$('#btn-clear-all-branches');if(btnAll)btnAll.addEventListener('click',()=>$('#user-form').querySelectorAll('input[name="userBranchItem"]').forEach(cb=>cb.checked=true));if(btnNone)btnNone.addEventListener('click',()=>$('#user-form').querySelectorAll('input[name="userBranchItem"]').forEach(cb=>cb.checked=false));$('#user-form').addEventListener('submit',async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));data.isActive=e.target.isActive.checked;const selectedBranches=[...e.target.querySelectorAll('input[name="userBranchItem"]:checked')].map(cb=>Number(cb.value));data.branchIds=selectedBranches;data.branchId=selectedBranches.length>0?selectedBranches[0]:null;data.room=data.room?data.room.trim():'';if(e.target.elements.assistantMachine)data.assistantMachine=data.role==='SuperAdmin'?e.target.elements.assistantMachine.value:'';data.permissions={};e.target.querySelectorAll('[data-permission]').forEach(input=>{if(input.value!=='inherit')data.permissions[input.dataset.permission]=input.value==='allow';});try{await api('/users'+(user.id?'/'+user.id:''),user.id?'PUT':'POST',data);closeModal();await loadPage();}catch(error){$('#form-error').textContent=error.message;}});}
function renderAudit(items){$('#content').innerHTML=`<div class="toolbar"><input id="audit-filter" placeholder="Поиск по ПК, администратору или команде">${can('audit.all')?'<button id="clear-audit" class="danger">Удалить журнал</button>':''}<small>Последние 500 записей</small></div><div id="audit-table"></div>`;const render=()=>{const q=$('#audit-filter').value.toLowerCase(),rows=items.filter(t=>(t.machineName+' '+t.adminUsername+' '+t.commandPayload).toLowerCase().includes(q));$('#audit-table').innerHTML=table(['ВРЕМЯ / АДМИНИСТРАТОР','КОМПЬЮТЕР','КОМАНДА','СТАТУС','РЕЗУЛЬТАТ'],rows.map(t=>`<tr><td>${date(t.timestamp)}<small>${escape(t.adminUsername)}</small></td><td>${escape(t.machineName)}</td><td class="wrap">${escape(t.commandType)}<small>${escape(t.commandPayload)}</small></td><td>${escape(t.status)}</td><td><button data-result="${t.id}">Открыть${t.exitCode==null?'':' · '+t.exitCode}</button></td></tr>`).join(''));$('#audit-table').querySelectorAll('[data-result]').forEach(b=>b.addEventListener('click',()=>{const t=items.find(i=>i.id===Number(b.dataset.result));modal('Результат · '+t.machineName,`<p>${escape(t.commandType)} · ${escape(t.status)} · Exit ${t.exitCode??'—'}</p><pre class="audit-output">${escape(t.result||'Ответ ещё не получен')}</pre>`);}));};$('#audit-filter').addEventListener('input',render);render();if($('#clear-audit'))$('#clear-audit').addEventListener('click',()=>remove('/audit','Безвозвратно удалить журнал?'));}
let ticketStatusFilter='all', ticketBranchFilter='';
const ticketStatusName=value=>({Created:'Новая',1:'Новая',2:'В работе (назначена)',3:'В работе (запланирована)',4:'Ожидание',5:'Выполнена (решена)',6:'Закрыта'})[value]||value;
function renderTickets(items,branches=[]){$('#content').innerHTML=`<div class="toolbar"><input id="ticket-filter" placeholder="Поиск по номеру, пользователю или тексту"><select id="ticket-status-filter">${[["all","Все статусы"],["active","Активные"],["work","В работе"],["1","Новая"],["2","В работе (назначена)"],["3","В работе (запланирована)"],["4","Ожидание"],["5","Выполнена (решена)"],["6","Закрыта"]].map(([value,label])=>`<option value="${value}" ${ticketStatusFilter===value?"selected":""}>${label}</option>`).join("")}</select><select id="ticket-branch-filter"><option value="">Все доступные филиалы</option><option value="0" ${ticketBranchFilter==='0'?'selected':''}>Без филиала (старые)</option>${branches.map(b=>`<option value="${b.id}" ${ticketBranchFilter===String(b.id)?'selected':''}>${escape(b.name)}</option>`).join('')}</select>${can('tickets.manage')?'<button id="new-ticket" class="primary">＋ Новая заявка</button>':''}</div><div id="ticket-table"></div>`;const render=()=>{const q=$('#ticket-filter').value.toLowerCase();const status=ticketStatusFilter;const visible=t=>status==='all'||(status==='active'&&Number(t.status==='Created'?1:t.status)<5)||(status==='work'&&['2','3'].includes(t.status))||String(t.status==='Created'?1:t.status)===status;$('#ticket-table').innerHTML=table(['GLPI','ПОЛЬЗОВАТЕЛЬ / ПК','ОБРАЩЕНИЕ','СТАТУС','СОЗДАНО',''],items.filter(visible).filter(t=>(t.glpiId+' '+t.username+' '+t.title+' '+t.description+' '+(t.branchName||'')+' '+(t.room||'')).toLowerCase().includes(q)).map(t=>`<tr><td><strong>#${t.glpiId}</strong></td><td>${escape(t.username)}<small>${escape(t.machineName)}</small><small>${escape(t.branchName||'Без филиала')} · кабинет ${escape(t.room||'—')}</small></td><td class="wrap">${escape(t.title||t.description.slice(0,70))}</td><td>${escape(ticketStatusName(t.status))}<small>${escape(t.assignedUsername|| (t.assignedGlpiUserId?"GLPI #"+t.assignedGlpiUserId:""))}</small></td><td>${date(t.createdAt)}</td><td><button data-ticket="${t.id}">Открыть</button></td></tr>`).join(''));$('#ticket-table').querySelectorAll('[data-ticket]').forEach(b=>b.addEventListener('click',()=>ticketForm(items.find(i=>i.id===Number(b.dataset.ticket)))));};$('#ticket-branch-filter').addEventListener('change',e=>{ticketBranchFilter=e.target.value;loadPage().catch(e=>toast(e.message,true));});$('#ticket-filter').addEventListener('input',render);$('#ticket-status-filter').addEventListener('change',e=>{ticketStatusFilter=e.target.value;loadPage().catch(e=>toast(e.message,true));});render();if($('#new-ticket'))$('#new-ticket').addEventListener('click',()=>{modal('Новое обращение',`<form id="ticket-form"><label>Филиал<select name="branchId" ${branches.some(b=>b.isActive)?'required':''}><option value="">Выберите филиал</option>${branches.filter(b=>b.isActive).map(b=>`<option value="${b.id}">${escape(b.name)}</option>`).join('')}</select></label><label>Кабинет<input name="room" maxlength="100" ${branches.some(b=>b.isActive)?'required':''}></label><label>Заголовок<input name="title" maxlength="160" required></label><label>Описание<textarea name="description" maxlength="8000" required></textarea></label><div class="error" id="form-error"></div><button class="primary">Создать в GLPI</button></form>`);$('#ticket-form').addEventListener('submit',async e=>{e.preventDefault();const send=e.target.querySelector('button');send.disabled=true;try{const data=Object.fromEntries(new FormData(e.target));data.branchId=data.branchId?Number(data.branchId):null;const result=await api('/tickets','POST',data);closeModal();await loadPage();toast('Создана заявка #'+result.glpiId);}catch(error){$('#form-error').textContent=error.message;}finally{send.disabled=false;}});});}
async function ticketForm(ticket){modal('Заявка GLPI #'+ticket.glpiId,`<p>${escape(ticket.username)} · ${escape(ticket.machineName)}</p><p>Филиал: ${escape(ticket.branchName||'Не указан')} · Кабинет: ${escape(ticket.room||'Не указан')}</p><pre class="audit-output">${escape(ticket.description)}</pre><div id="ticket-attachments-area"></div><div id="glpi-status"><small>Загрузка данных GLPI…</small></div>${can('tickets.manage')?'<form id="ticket-status-form" class="terminal-input"><select name="status"><option value="1">Новая</option><option value="2">В работе (назначена)</option><option value="3">В работе (запланирована)</option><option value="4">Ожидание</option><option value="5">Выполнена (решена)</option><option value="6">Закрыта</option></select><button class="primary">Изменить статус</button></form>':''}`);try{const detail=await api('/tickets/'+ticket.id);if($('#glpi-status'))$('#glpi-status').textContent='Текущий статус GLPI: '+ticketStatusName(detail.glpi.status);if(detail.attachments&&detail.attachments.length&&$('#ticket-attachments-area')){$('#ticket-attachments-area').innerHTML=`<div class="ticket-attachments-box" style="margin:12px 0;padding:10px;background:var(--bg-subtle,#f8fafc);border-radius:8px;border:1px solid var(--border,#e2e8f0)"><h4 style="margin:0 0 8px 0;font-size:13px">📎 Прикреплённые файлы (${detail.attachments.length})</h4><div style="display:flex;flex-wrap:wrap;gap:8px">${detail.attachments.map(a=>{const isImg=/\.(png|jpe?g|gif|bmp|webp)$/i.test(a.fileName);const url=`/api/tickets/${ticket.id}/attachments/${a.id}`;return `<div style="border:1px solid #cbd5e1;background:#fff;border-radius:6px;padding:6px 10px;display:flex;align-items:center;gap:8px">${isImg?`<a href="${url}" target="_blank"><img src="${url}" style="width:36px;height:36px;object-fit:cover;border-radius:4px" alt="${escape(a.fileName)}"></a>`:`<span style="font-size:18px">📄</span>`}<div><a href="${url}" download="${escape(a.fileName)}" style="font-weight:600;font-size:12px;text-decoration:none;color:#2563eb">${escape(a.fileName)}</a><div style="font-size:11px;color:#64748b">${(a.fileSize/1024).toFixed(1)} КБ</div></div></div>`;}).join('')}</div></div>`;}}catch(e){if($('#glpi-status'))$('#glpi-status').textContent=e.message;}if($('#ticket-status-form'))$('#ticket-status-form').addEventListener('submit',async e=>{e.preventDefault();try{await api('/tickets/'+ticket.id+'/status','PUT',{status:Number(new FormData(e.target).get('status'))});closeModal();await loadPage();toast('Статус обновлён');}catch(error){toast(error.message,true);}});}
async function remove(path,text){if(!confirm(text))return;try{await api(path,'DELETE');await loadPage();}catch(e){toast(e.message,true);}}
if(token)enter().catch(()=>logout());

async function waitProcessTask(task, root) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (!root.isConnected || !$('#modal').open) throw new Error('Окно закрыто');
    const result = await api('/tasks/' + encodeURIComponent(task.taskId));
    if (result.status !== 'Pending') {
      if (result.status !== 'Completed' || result.exitCode !== 0) throw new Error(result.result || 'Не удалось выполнить запрос: ' + result.status);
      return result.result;
    }
    await new Promise(resolve => setTimeout(resolve, 400));
  }
  throw new Error('ПК не ответил за 30 секунд. Проверьте связь и обновите клиент помощника.');
}
async function processForm(c) {
  if (!c.isOnline) throw new Error('Компьютер не в сети');
  modal('Процессы · ' + c.machineName, '<div id="process-panel"><div class="toolbar"><input id="process-filter" placeholder="Фильтр по имени или PID" aria-label="Фильтр процессов"><button id="process-refresh">↻ Обновить</button></div><p id="process-status" role="status"></p><div id="process-list"></div><p><small>Выберите один экземпляр процесса. Доступ определяется правами пользователя помощника.</small></p><button id="process-stop" class="danger" disabled>Остановить выбранный процесс</button></div>');
  const root = $('#process-panel'), filter = $('#process-filter'), refresh = $('#process-refresh'), stop = $('#process-stop'), status = $('#process-status'), list = $('#process-list');
  let rows = [], choice = null, busy = false;
  function render() {
    const q = filter.value.trim().toLowerCase();
    const visible = rows.filter(p => p.name.toLowerCase().includes(q) || String(p.pid).includes(q));
    if (choice && !visible.some(p => p.pid === choice.pid)) choice = null;
    list.innerHTML = table(['', 'ИМЯ ПРОЦЕССА', 'PID', 'ПАМЯТЬ'], visible.map(p => `<tr><td><input type="radio" name="selected-process" data-pid="${p.pid}" aria-label="Выбрать ${escape(p.name)}, PID ${p.pid}" ${choice?.pid===p.pid?'checked':''} ${busy||!p.canStop?'disabled':''}></td><td>${escape(p.name)}${p.canStop?'':'<small>Остановка недоступна</small>'}</td><td>${p.pid}</td><td>${p.memoryBytes==null?'—':(p.memoryBytes/1048576).toFixed(1)+' МиБ'}</td></tr>`).join(''));
    list.style.maxHeight = '360px'; list.style.overflow = 'auto';
    list.querySelectorAll('[data-pid]').forEach(input => input.addEventListener('change', () => { choice = rows.find(p => p.pid === Number(input.dataset.pid)); stop.disabled = busy || !choice?.canStop; }));
    stop.disabled = busy || !choice?.canStop;
    refresh.disabled = busy;
  }
  async function load() {
    busy = true; choice = null; status.textContent = 'Получение процессов с компьютера…'; render();
    try {
      const [task] = await api('/commands', 'POST', { machines: [c.machineName], type: 'processes', payload: '' });
      const output = await waitProcessTask(task, root);
      if (!root.isConnected) return;
      const received = JSON.parse(output);
      if (!Array.isArray(received) || received.some(p => !Number.isInteger(p.pid) || typeof p.name !== 'string')) throw new Error('Некорректный список процессов');
      rows = received; status.textContent = 'Получено процессов: ' + rows.length;
    } catch (error) { if (root.isConnected) status.textContent = error.message; }
    finally { busy = false; if (root.isConnected) render(); }
  }
  filter.addEventListener('input', render);
  refresh.addEventListener('click', load);
  stop.addEventListener('click', async () => {
    if (busy || !choice?.canStop) return;
    const target = choice;
    if (!confirm(`Остановить ${target.name}, PID ${target.pid}, на ${c.machineName}? Несохранённые данные могут быть потеряны.`)) return;
    busy = true; status.textContent = 'Остановка выбранного процесса…'; render();
    try {
      const [task] = await api('/commands', 'POST', { machines: [c.machineName], type: 'kill_pid', payload: JSON.stringify({ pid: target.pid, name: target.name, startTimeUtcTicks: target.startTimeUtcTicks }) });
      toast(await waitProcessTask(task, root));
      if (root.isConnected) await load();
    } catch (error) { if (root.isConnected) status.textContent = error.message; }
    finally { busy = false; if (root.isConnected) render(); }
  });
  await load();
}

const settingsHelp = {
  glpi: {
    baseUrl: 'Полный URL GLPI, включая /apirest.php при нестандартном пути. Сервер обращается к этому адресу из контейнера, поэтому localhost клиента здесь не подходит.',
    authMode: 'User Token подходит для готового API-токена. Логин и пароль создают сессию GLPI от сервисной учётной записи и требуют прав на поиск пользователей и создание заявок.',
    appToken: 'Необязательный App Token API-клиента GLPI. Пустое поле сохраняет ранее записанный токен; для удаления отметьте пункт ниже.',
    clearAppToken: 'Удаляет сохранённый App Token после сохранения формы.',
    userToken: 'User Token пользователя GLPI для режима токена. Пустое поле сохраняет прежний токен.',
    clearUserToken: 'Удаляет сохранённый User Token после сохранения формы.',
    login: 'Логин сервисной учётной записи GLPI для режима логина и пароля.',
    password: 'Пароль сервисной учётной записи GLPI. Хранится зашифрованным; пустое поле сохраняет прежний пароль.',
    clearPassword: 'Удаляет сохранённый пароль GLPI после сохранения формы.',
    serviceUserId: 'ID пользователя GLPI, от имени которого создаётся заявка, если AD-логин заявителя не найден. 0 отключает резервный вариант.'
  },
  telegram: {
    enabled: 'Отправлять карточки новых заявок в выбранную группу Telegram. Сбой Telegram не блокирует создание заявки.',
    botToken: 'Токен бота из BotFather. Хранится зашифрованным; пустое поле сохраняет прежний токен.',
    clearBotToken: 'Удаляет сохранённый токен бота. Уведомления перестанут отправляться до указания нового токена.',
    chatId: 'ID группы или канала Telegram, куда бот отправляет заявки. Для супергруппы обычно начинается с -100.',
    managementEnabled: 'Разрешает исполнителям брать заявки в работу и менять статус командами бота после входа через AD.',
    idAttribute: 'LDAP-атрибут, в котором хранится Telegram ID сотрудника. По умолчанию physicalDeliveryOfficeName.',
    directoryLogin: 'Учётная запись AD только для чтения атрибутов Telegram ID. Укажите UPN или DN.',
    directoryPassword: 'Пароль учётной записи чтения AD. Хранится зашифрованным; пустое поле сохраняет прежний.',
    clearDirectoryPassword: 'Удаляет сохранённый пароль учётной записи чтения AD.',
    threadId: 'ID темы внутри выбранной группы. 0 отправляет в общий чат.'
  },
  ad: {
    enabled: 'Разрешает сотрудникам входить в панель и клиент по логину и паролю Active Directory.',
    host: 'DNS-имя контроллера домена, например dc01.gp1.loc. Соединение всегда идёт по LDAPS.',
    port: 'Порт LDAPS контроллера домена. Обычно 636.',
    domain: 'DNS-имя домена AD, например gp1.loc. Используется для проверки учётных данных.',
    netbiosDomain: 'Короткое NetBIOS-имя домена, например GP1. Нужно для входа в формате DOMAIN\\логин.',
    baseDn: 'База поиска пользователей в LDAP, например DC=gp1,DC=loc. Поиск выполняется только внутри этой ветки.',
    caCertificate: 'PEM-сертификат корневого или промежуточного CA, если он не установлен в доверенные на сервере. Нужен для проверки LDAPS.'
  },
  updates: {
    'updates-enabled': 'Разрешает клиентам скачивать опубликованные подписанные обновления с этого сервера.',
    'updates-package': 'Подписанный MSI новой версии помощника. Сервер проверит подпись, размер и SHA-256 перед публикацией.',
    'updates-manifest': 'Подписанный JSON-манифест PixelHelper.update.json с версией, хэшем и метаданными пакета.',
    package: 'Подписанный MSI новой версии помощника. Сервер проверит подпись, размер и SHA-256 перед публикацией.',
    manifest: 'Подписанный JSON-манифест PixelHelper.update.json с версией, хэшем и метаданными пакета.'
  },
  branches: {
    name: 'Название филиала, которое видят пользователи в заявках, оповещениях и настройках.',
    computerPrefixes: 'Первые буквы имён компьютеров этого филиала через запятую или пробел. Используются для автоназначения филиала.',
    ipSubnets: 'IP-подсети филиала через запятую. Применяются, когда префиксы ПК совпадают у нескольких филиалов.',
    isActive: 'Отключённый филиал нельзя выбрать для новых заявок, но история и настройки сохраняются.'
  },
  messenger: {
    enabled: 'Включает личную переписку сотрудников в клиенте помощника.',
    retentionDays: 'Срок хранения сообщений в днях. 0 — хранить без ограничения; иначе допустимо от 30 до 3650 дней.',
    unreadReminderMinutes: 'Как часто напоминать сотруднику о непрочитанных сообщениях. Допустимо от 1 до 1440 минут.'
  },
  cartridge: {
    enabled: 'Разрешает внешней системе учёта картриджей отправлять уведомления на компьютеры через помощника.',
    apiKey: 'Необязательный секретный ключ, который внешняя система передаёт в запросе. Пустое значение отключает проверку ключа.'
  },
  emergency: {
    enabled: 'Включает интеграцию экстренных оповещений с сервером AudioRONGTA.',
    serverUrl: 'Адрес сервера AudioRONGTA, доступный с сервера помощника, например http://172.16.16.63:8085.',
    apiKey: 'Мастер-ключ или X-Client-Key сервера AudioRONGTA. Пустое значение использует авторизованный токен моста.',
    allowStandalone: 'Разрешает помощникам показывать тревогу напрямую, если сервер AudioRONGTA недоступен.',
    allowClientTrigger: 'Позволяет сотрудникам запускать тревогу из меню в трее клиента помощника.',
    broadcastAllBranches: 'Отправлять массовые экстренные коды во все филиалы, а не только в текущий корпус.',
    telegramAlertsEnabled: 'Дублировать экстренные коды и вызовы «Код Синий» в Telegram дежурным специалистам.',
    telegramChatId: 'ID Telegram-чата для экстренных оповещений. Пустое значение использует основной чат техподдержки.',
    telegramThreadId: 'ID темы внутри Telegram-чата для экстренных сообщений. 0 — общий чат.',
    'manual-alert-code': 'Тип экстренного кода для ручного запуска с этой страницы.',
    'manual-alert-branch': 'Филиал, в котором запускается ручное оповещение. Обязателен, если массовая рассылка по филиалам отключена.',
    'manual-alert-cabinet': 'Кабинет или место происшествия, которое увидят на экранах оповещения.',
    'manual-alert-notes': 'Дополнительная инструкция или комментарий для отображения на экранах.'
  },
  medical: {
    key: 'Стабильный технический ключ статуса. Используется интеграциями; после создания не меняется.',
    title: 'Название статуса, которое отображается сотрудникам.',
    emoji: 'Эмодзи перед названием статуса в интерфейсе клиента.',
    colorHex: 'Цвет статуса в интерфейсе и на индикаторах.',
    orderIndex: 'Порядок сортировки в списке. Меньшее значение показывается выше.',
    description: 'Пояснение для администратора и пользователей о смысле статуса.',
    muteSounds: 'Отключает звуковые уведомления при переходе в этот статус.',
    muteTts: 'Отключает голосовое озвучивание этого статуса.',
    isActive: 'Отключённый статус остаётся в истории, но не предлагается для новых записей.'
  }
};

function decorateSettingsHelp(root, scope) {
  const help = settingsHelp[scope];
  if (!root || !help || typeof window.fieldHelp !== 'function') return;
  root.querySelectorAll('input,select,textarea').forEach(field => {
    if (field.type === 'hidden') return;
    const key = field.getAttribute('name') || field.id;
    const text = help[key];
    if (!text) return;
    const label = field.closest('label');
    if (!label || label.querySelector('[data-field-help]')) return;
    const title = (label.textContent || key).replace(/\s+/g, ' ').trim().slice(0, 100);
    label.insertAdjacentHTML('beforeend', window.fieldHelp(title, text));
  });
}

function renderGlpiSettings(settings) {
  ($('#settings-tab-body')||$('#content')).innerHTML = `<div class="panel glpi-settings-panel"><h2>Подключение к GLPI</h2><p>Настройки применяются сразу, без перезапуска Docker. Сохранённые токены и пароль не возвращаются в браузер.</p><form id="glpi-settings-form"><label>Адрес сайта или REST API<input name="baseUrl" type="url" maxlength="1000" value="${escape(settings.baseUrl)}" placeholder="http://glpi.gp1.loc" required></label><label>Способ API-входа<select name="authMode"><option value="token" ${settings.authMode==='token'?'selected':''}>User Token</option><option value="password" ${settings.authMode==='password'?'selected':''}>Логин и пароль сервисной учётной записи</option></select></label><label>App Token (если требуется API-клиентом GLPI)<input name="appToken" type="password" autocomplete="new-password" placeholder="${settings.hasAppToken?'Сохранён · пустое поле оставляет прежний':'Не задан'}" maxlength="2000"></label><label class="check-label"><input name="clearAppToken" type="checkbox">Удалить сохранённый App Token</label><div id="glpi-token-fields"><label>User Token<input name="userToken" type="password" autocomplete="new-password" placeholder="${settings.hasUserToken?'Сохранён · пустое поле оставляет прежний':'Не задан'}" maxlength="2000"></label><label class="check-label"><input name="clearUserToken" type="checkbox">Удалить сохранённый User Token</label></div><div id="glpi-password-fields"><label>Логин GLPI<input name="login" value="${escape(settings.login)}" maxlength="150" autocomplete="off"></label><label>Пароль GLPI<input name="password" type="password" autocomplete="new-password" placeholder="${settings.hasPassword?'Сохранён · пустое поле оставляет прежний':'Не задан'}" maxlength="2000"></label><label class="check-label"><input name="clearPassword" type="checkbox">Удалить сохранённый пароль</label></div><label>ID заявителя по умолчанию<input name="serviceUserId" type="number" min="0" step="1" value="${settings.serviceUserId}"></label><small>Если AD-логин пользователя не найден в GLPI, заявка создаётся от этого пользователя GLPI. Значение 0 запрещает такой fallback.</small><div class="form-actions"><button class="primary" type="submit">Сохранить</button><button id="glpi-test" type="button">Сохранить и проверить подключение</button></div><p id="glpi-result" role="status"></p></form><h3>Что должно быть включено в GLPI</h3><p>Настройка → Общие → API: REST API, выбранный способ API-входа и API-клиент с разрешённым IP сервера помощника. Для сервисной учётной записи нужны права на поиск пользователей и создание заявок в нужной сущности.</p><p><small>Автовход в браузере не передаёт серверу API-сессию. Проверка подключения не создаёт заявки; права на создание проверяются при настоящей отправке.</small></p></div>`;
  const form=$('#glpi-settings-form'), result=$('#glpi-result');
  const fields=()=>{ $('#glpi-token-fields').hidden=form.elements.authMode.value!=='token'; $('#glpi-password-fields').hidden=form.elements.authMode.value!=='password'; };
  form.elements.authMode.addEventListener('change',fields); fields(); decorateSettingsHelp(form,'glpi');
  async function save(test) {
    if(!form.reportValidity())return;
    const data=Object.fromEntries(new FormData(form)); data.serviceUserId=Number(data.serviceUserId);
    for(const name of ['clearAppToken','clearUserToken','clearPassword'])data[name]=form.elements[name].checked;
    form.querySelectorAll('button').forEach(button=>button.disabled=true);result.className='';result.textContent=test?'Сохранение и проверка GLPI…':'Сохранение…';
    try {
      const saved=await api('/settings/glpi','PUT',data);
      const response=test?await api('/settings/glpi/test','POST',{}):{success:true,message:'Настройки GLPI сохранены.'};
      if(!form.isConnected)return;
      renderGlpiSettings(saved);$('#glpi-result').textContent=response.message;$('#glpi-result').className=response.success?'':'error';
    }catch(error){if(form.isConnected){result.textContent=error.message;result.className='error';}}
    finally{if(form.isConnected)form.querySelectorAll('button').forEach(button=>button.disabled=false);}
  }
  form.addEventListener('submit',event=>{event.preventDefault();save(false);});$('#glpi-test').addEventListener('click',()=>save(true));
}

async function refreshLoginProviders(){try{const value=await api('/auth/providers');$('#login-provider option[value="ad"]').hidden=!value.adEnabled;}catch{}}
refreshLoginProviders();

function integrationForm(kind, fields, description, read, extra='') {
  ($('#settings-tab-body')||$('#content')).innerHTML=`<div class="panel glpi-settings-panel"><h2>${kind==='ad'?'Вход через Active Directory':'Уведомления Telegram'}</h2><p>${description}</p><form id="integration-form">${fields}<div class="form-actions"><button class="primary" type="submit">Сохранить</button><button id="integration-test" type="button">${kind==='ad'?'Сохранить и проверить LDAPS':'Сохранить и отправить тестовое сообщение'}</button></div><p id="integration-result" role="status"></p></form>${extra}</div>`;
  const form=$('#integration-form'), result=$('#integration-result');
  const save=async test=>{if(!form.reportValidity())return;form.querySelectorAll('button').forEach(b=>b.disabled=true);try{await api('/settings/'+kind,'PUT',read(form));const response=test?await api('/settings/'+kind+'/test','POST',{}):{success:true,message:'Настройки сохранены.'};if(form.isConnected){result.textContent=response.message;result.className=response.success?'':'error';if(form.elements.botToken)form.elements.botToken.value='';if(form.elements.clearBotToken)form.elements.clearBotToken.checked=false;}if(kind==='ad')refreshLoginProviders();}catch(e){if(form.isConnected){result.textContent=e.message;result.className='error';}}finally{if(form.isConnected)form.querySelectorAll('button').forEach(b=>b.disabled=false);}};
  form.addEventListener('submit',e=>{e.preventDefault();save(false);});$('#integration-test').addEventListener('click',()=>save(true));
  decorateSettingsHelp(form,kind);
}
function renderSuperButtons(items){
  $('#content').innerHTML=`<div class="toolbar"><button id="new-super-button" class="primary">＋ Новая кнопка супер админа</button><button id="apply-super-buttons">Применить</button></div><p>Эти кнопки доступны только в привязанном помощнике после входа SuperAdmin. Пустой текст позволяет написать сообщение при отправке.</p>${table(['НАЗВАНИЕ','СООБЩЕНИЕ','ПОЛУЧАТЕЛИ','ПОКАЗ',''],items.map(b=>`<tr><td>${escape(b.title)}<small>${b.isActive?'Активна':'Отключена'} · порядок ${b.orderIndex}</small></td><td class="wrap">${escape(b.message||'Ввод при отправке')}</td><td>${b.target==='all'?'Все':'Выбор ПК'}</td><td>${b.durationSeconds} сек.</td><td><button data-edit-super="${b.id}">Изменить</button><button data-delete-super="${b.id}" class="danger">Удалить</button></td></tr>`).join(''))}`;
  $('#new-super-button').addEventListener('click',()=>superButtonForm());
  $('#apply-super-buttons').addEventListener('click',async()=>{try{await api('/super-buttons/apply','POST',{});toast('Настройки применены. Для обновления списка откройте окно супер админа заново.');}catch(e){toast(e.message,true);}});
  $('#content').querySelectorAll('[data-edit-super]').forEach(b=>b.addEventListener('click',()=>superButtonForm(items.find(i=>i.id===Number(b.dataset.editSuper)))));
  $('#content').querySelectorAll('[data-delete-super]').forEach(b=>b.addEventListener('click',()=>remove('/super-buttons/'+b.dataset.deleteSuper,'Удалить кнопку?')));
}
function superButtonForm(b={title:'',message:'',target:'select',durationSeconds:30,orderIndex:0,isActive:true}){
  modal(b.id?'Кнопка супер админа':'Новая кнопка супер админа',`<form id="super-button-form"><label>Название<input name="title" value="${escape(b.title)}" maxlength="100" required></label><label>Шаблон сообщения<textarea name="message" maxlength="1000">${escape(b.message)}</textarea></label><label>Получатели по умолчанию<select name="target"><option value="select" ${b.target==='select'?'selected':''}>Выбор ПК</option><option value="all" ${b.target==='all'?'selected':''}>Все клиенты</option></select></label><div class="form-row"><label>Время показа, секунд<input name="durationSeconds" type="number" min="10" max="300" value="${b.durationSeconds}" required></label><label>Порядок<input name="orderIndex" type="number" min="0" max="10000" value="${b.orderIndex}" required></label></div><label class="check-label"><input name="isActive" type="checkbox" ${b.isActive?'checked':''}>Активна</label><div id="form-error" class="error"></div><button class="primary">Сохранить</button></form>`);
  $('#super-button-form').addEventListener('submit',async e=>{e.preventDefault();const data=Object.fromEntries(new FormData(e.target));data.isActive=e.target.isActive.checked;data.durationSeconds=Number(data.durationSeconds);data.orderIndex=Number(data.orderIndex);try{await api('/super-buttons'+(b.id?'/'+b.id:''),b.id?'PUT':'POST',data);closeModal();await loadPage();}catch(error){$('#form-error').textContent=error.message;}});
}
function renderMessages(pcs,buttons){
  $('#content').innerHTML=`<div class="panel"><form id="message-form"><label>Шаблон<select id="message-template"><option value="">Произвольное сообщение</option>${buttons.filter(b=>b.isActive).map(b=>`<option value="${b.id}">${escape(b.title)}</option>`).join('')}</select></label><label>Сообщение<textarea name="text" maxlength="1000" required placeholder="Текст в облачке над помощником"></textarea></label><label>Время показа, секунд<input name="durationSeconds" type="number" min="10" max="300" value="30" required></label><label class="check-label"><input id="message-all" type="checkbox">Все зарегистрированные клиенты</label><input id="message-filter" placeholder="Поиск по имени ПК"><div id="message-targets"></div><p><small>Нужен клиент 1.2.3 или новее. Офлайн-клиенты отмечаются в журнале; сообщения для них не откладываются. Подтверждение означает создание облачка, а не прочтение человеком.</small></p><div id="message-error" class="error"></div><button class="primary">Отправить</button></form><div id="message-results"></div></div>`;
  const chosen=new Set(),form=$('#message-form');
  const render=()=>{const q=$('#message-filter').value.toLowerCase();$('#message-targets').innerHTML=table(['','КОМПЬЮТЕР','СОСТОЯНИЕ'],pcs.filter(c=>c.machineName.toLowerCase().includes(q)).map(c=>`<tr><td><input type="checkbox" data-message-machine="${escape(c.machineName)}" ${chosen.has(c.machineName)?'checked':''} ${$('#message-all').checked?'disabled':''}></td><td>${escape(c.machineName)}</td><td>${c.isOnline?'Онлайн':'Офлайн'}</td></tr>`).join(''));$('#message-targets').querySelectorAll('[data-message-machine]').forEach(c=>c.addEventListener('change',()=>c.checked?chosen.add(c.dataset.messageMachine):chosen.delete(c.dataset.messageMachine)));};
  $('#message-filter').addEventListener('input',render);$('#message-all').addEventListener('change',render);render();
  $('#message-template').addEventListener('change',e=>{const b=buttons.find(x=>x.id===Number(e.target.value));if(b){form.elements.text.value=b.message;form.elements.durationSeconds.value=b.durationSeconds;$('#message-all').checked=b.target==='all';render();}});
  form.addEventListener('submit',async e=>{e.preventDefault();const send=form.querySelector('button');send.disabled=true;$('#message-error').textContent='';try{const machines=$('#message-all').checked?['ALL']:[...chosen];if(machines.length===0)throw new Error('Выберите получателей');const results=await api('/messages','POST',{machines,text:form.elements.text.value,durationSeconds:Number(form.elements.durationSeconds.value)});$('#message-results').innerHTML=`<p>Передано: ${results.filter(r=>r.status==='Pending').length}; недоступно: ${results.filter(r=>r.status==='Offline').length}. Подтверждения показа — в журнале аудита.</p>${table(['ПК','СОСТОЯНИЕ'],results.map(r=>`<tr><td>${escape(r.machineName)}</td><td>${escape(r.status)}</td></tr>`).join(''))}`;toast('Отправка зарегистрирована');}catch(error){$('#message-error').textContent=error.message;}finally{send.disabled=false;}});
}
function renderTelegramSettings(data){
  queueMicrotask(()=>renderGlpiImportSettings().catch(e=>toast(e.message,true)));
  const s=data.settings;
  integrationForm('telegram',`<label class="check-label"><input name="enabled" type="checkbox" ${s.enabled?'checked':''}>Отправлять новые заявки в Telegram</label><label>Токен бота<input name="botToken" type="password" autocomplete="new-password" maxlength="300" placeholder="${s.hasBotToken?'Сохранён · пустое поле сохраняет прежний':'123456:токен из BotFather'}"></label><label class="check-label"><input name="clearBotToken" type="checkbox">Удалить сохранённый токен</label><label>Chat ID группы<input name="chatId" value="${escape(s.chatId)}" placeholder="-1001234567890" maxlength="100"></label><label class="check-label"><input name="managementEnabled" type="checkbox" ${s.managementEnabled?'checked':''}>Управлять заявками через бота</label><label>LDAP-атрибут Telegram ID<input name="idAttribute" value="${escape(s.idAttribute||'physicalDeliveryOfficeName')}" maxlength="64"></label><label>Учётная запись чтения AD (UPN или DN)<input name="directoryLogin" value="${escape(s.directoryLogin||'')}" maxlength="256" placeholder="svc-helper@gp1.loc"></label><label>Пароль учётной записи чтения AD<input name="directoryPassword" type="password" autocomplete="new-password" maxlength="256" placeholder="${s.hasDirectoryPassword?'Сохранён · пустое поле сохраняет прежний':'Пароль'}"></label><label class="check-label"><input name="clearDirectoryPassword" type="checkbox">Удалить пароль чтения AD</label><label>ID темы (0 — общий чат)<input name="threadId" type="number" min="0" step="1" value="${s.threadId}"></label>`,
    'Добавьте бота в группу и разрешите ему отправлять сообщения. В группу попадут номер заявки, пользователь, компьютер и текст обращения. Сбой Telegram не отменяет заявку: очередь сохраняется и повторяет отправку.',
    f=>({enabled:f.elements.enabled.checked,botToken:f.elements.botToken.value,clearBotToken:f.elements.clearBotToken.checked,chatId:f.elements.chatId.value.trim(),threadId:Number(f.elements.threadId.value),managementEnabled:f.elements.managementEnabled.checked,directoryLogin:f.elements.directoryLogin.value.trim(),directoryPassword:f.elements.directoryPassword.value,clearDirectoryPassword:f.elements.clearDirectoryPassword.checked,idAttribute:f.elements.idAttribute.value.trim()}),
    `<p>Для управления: каждый исполнитель входит в панель через AD и получает права «Просмотр всех заявок» и «Создание заявок и изменение статуса». В GLPI должен быть активный пользователь с тем же логином AD. Откройте бота → Start → /my. Бот должен использоваться только этим сервером, без webhook.</p><div class="form-row"><label>Telegram ID для проверки AD / GLPI<input id="telegram-probe-id" type="text" inputmode="numeric" placeholder="684257380"></label><button id="telegram-probe" type="button">Проверить сопоставление</button></div><p>Последняя проверка бота: ${data.botLastPollAt?date(data.botLastPollAt):"Не выполнялась"}.</p>${data.botLastError?`<p class="error">${escape(data.botLastError)}</p>`:""}<p>В очереди: ${data.pending}. Ошибок после 10 попыток: ${data.failed}.</p>${data.lastError?`<p class="error">${escape(data.lastError)}</p>`:''}<button id="telegram-retry" type="button">Повторить неотправленные сообщения</button><p><small>Telegram применяется к новым заявкам после включения. Токен хранится зашифрованным. Тестовая отправка работает и при отключённых уведомлениях.</small></p>`);
  $('#telegram-probe').addEventListener('click',async()=>{try{const telegramId=Number($('#telegram-probe-id').value);if(!Number.isSafeInteger(telegramId)||telegramId<=0)throw new Error('Укажите положительный Telegram ID');const r=await api('/settings/telegram/directory/test','POST',{telegramId});toast(r.message);}catch(e){toast(e.message,true);}});
  $('#telegram-retry').addEventListener('click',async()=>{try{const r=await api('/settings/telegram/retry','POST',{});toast('Возвращено в очередь: '+r.count);await loadPage();}catch(e){toast(e.message,true);}});
}
function renderAdSettings(s){
  integrationForm('ad',`<label class="check-label"><input name="enabled" type="checkbox" ${s.enabled?'checked':''}>Разрешить вход по логину и паролю AD</label><label>Контроллер домена (DNS-имя)<input name="host" value="${escape(s.host)}" placeholder="dc01.gp1.loc" maxlength="253" required></label><label>Порт LDAPS<input name="port" type="number" min="1" max="65535" value="${s.port}" required></label><label>DNS-домен AD<input name="domain" value="${escape(s.domain)}" placeholder="gp1.loc" maxlength="150" required></label><label>Короткое имя домена<input name="netbiosDomain" value="${escape(s.netbiosDomain)}" placeholder="GP1" maxlength="30"></label><label>Base DN<input name="baseDn" value="${escape(s.baseDn)}" placeholder="DC=gp1,DC=loc" maxlength="1000" required></label><label>Сертификат CA в PEM (если CA отсутствует в доверенных сертификатах сервера)<textarea name="caCertificate" rows="7" maxlength="30000" placeholder="-----BEGIN CERTIFICATE-----">${escape(s.caCertificate)}</textarea></label>`,
    'Пользователь вводит логин и пароль AD. Первый вход создаёт учётную запись с ролью User: только просмотр своих заявок. Пароли AD не сохраняются. Роль и блокировку может менять суперадминистратор в разделе пользователей.',
    f=>({enabled:f.elements.enabled.checked,host:f.elements.host.value.trim(),port:Number(f.elements.port.value),domain:f.elements.domain.value.trim(),netbiosDomain:f.elements.netbiosDomain.value.trim(),baseDn:f.elements.baseDn.value.trim(),caCertificate:f.elements.caCertificate.value.trim()}),
    '<p><small>Поддерживаются логин, DOMAIN\\логин и логин@домен. Соединение с AD всегда LDAPS с проверкой сертификата. При изменении настроек AD доменные пользователи должны войти заново. Для передачи паролей от браузера настройте HTTPS на веб-панели.</small></p>');
}

function renderClientUpdates(data){
  ($('#settings-tab-body')||$('#content')).innerHTML=`<div class="panel glpi-settings-panel"><h2>Автоматическое обновление помощника</h2><p>Служба обновлений в MSI 1.2.0 и новее проверяет сервер каждый час. Перед установкой она проверяет подпись, размер и SHA-256. Адрес сервера, ключ регистрации и настройки пользователя сохраняются.</p><label class="check-label"><input id="updates-enabled" type="checkbox" ${data.enabled?'checked':''}>Выдавать обновления клиентам</label><button id="save-update-switch" type="button">Сохранить</button><p>Опубликованная версия: <strong>${escape(data.latest?.version||'Нет')}</strong>${data.latest?` · ${(data.latest.size/1048576).toFixed(1)} МБ`:''}</p><form id="client-update-form"><label>MSI клиента<input name="package" type="file" accept=".msi" required></label><label>Подписанный манифест PixelHelper.update.json<input name="manifest" type="file" accept=".json" required></label><button class="primary">Проверить и опубликовать обновление</button><p id="client-update-result" role="status"></p></form><p><small>Сначала установите MSI со службой обновлений на компьютеры. Клиенты 1.1.3 и старше требуют однократной установки новой версии. Будущие версии устанавливаются без участия пользователя. Сборка с ключом подписи создаёт MSI и JSON; закрытый ключ на сервер и в GitHub не загружается.</small></p></div>`;
  $('#save-update-switch').addEventListener('click',async()=>{try{await api('/settings/updates','PUT',{enabled:$('#updates-enabled').checked});toast('Настройка сохранена');}catch(e){toast(e.message,true);}});
  $('#client-update-form').addEventListener('submit',async e=>{e.preventDefault();const form=e.target,button=form.querySelector('button'),result=$('#client-update-result');button.disabled=true;result.textContent='Загрузка и проверка пакета…';try{const response=await fetch('/api/settings/updates/upload',{method:'POST',headers:{Authorization:'Bearer '+token},body:new FormData(form)});const value=await response.json();if(!response.ok)throw new Error(value.error||'Ошибка '+response.status);await loadPage();toast('Опубликована версия '+value.latest.version);}catch(error){if(form.isConnected){result.textContent=error.message;result.className='error';}}finally{if(form.isConnected)button.disabled=false;}});
  decorateSettingsHelp($('#settings-tab-body'),'updates');
}

function renderSecuritySettings(data){
  const host = ($('#settings-tab-body')||$('#content'));
  const s = data.settings || data;
  const av = data.antivirusStatus || {};
  const manifest = av.manifest || {};

  host.innerHTML = `<div class="panel glpi-settings-panel">
    <h2>Безопасность и защита рабочих станций</h2>
    <p>Централизованное управление защитой от фишинга (WebGuard) и антивирусным модулем PixelGuard AV (проверка USB, эвристика, сигнатуры, изоляция в карантин и доверие Windows Defender).</p>

    <!-- ВЕБ-ЗАЩИТА WEBGUARD -->
    <div style="background:var(--card-bg,#fff);border:1px solid var(--border,#CBD5E1);border-radius:8px;padding:1.25rem;margin-bottom:1.5rem">
      <h3 style="margin-top:0;display:flex;align-items:center;gap:8px">🛡️ Защита от фишинговых ссылок (WebGuard)</h3>
      <p style="font-size:0.95rem;color:var(--text-muted,#475569);margin-bottom:1rem">
        <strong>При включении:</strong> интерактивный помощник назначает себя обработчиком веб-ссылок, открывает безопасные сайты в браузере по умолчанию и блокирует фишинговые ресурсы.<br>
        <strong>При выключении:</strong> системным браузером по умолчанию восстанавливается стандартный браузер пользователя.
      </p>
      <label class="check-label" style="font-size:1.05rem;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:8px">
        <input id="webguard-enabled" type="checkbox" ${s.webGuardEnabled !== false ? 'checked' : ''}>
        Включить проверку фишинговых ссылок на всех ПК
      </label>
      <div style="margin-top:1rem;display:flex;gap:1rem;align-items:center">
        <a id="btn-download-gpo" href="/api/settings/security/gpo-script" download="Deploy-WebGuardGpo.ps1" class="secondary" style="display:inline-flex;align-items:center;gap:6px;text-decoration:none;padding:6px 12px;border-radius:6px;font-size:0.9rem">
          ⬇ Скачать GPO-скрипт WebGuard (.ps1)
        </a>
      </div>
    </div>

    <!-- АНТИВИРУСНЫЙ МОДУЛЬ PIXELGUARD AV -->
    <div style="background:var(--card-bg,#fff);border:1px solid var(--border,#CBD5E1);border-radius:8px;padding:1.25rem;margin-bottom:1.5rem">
      <h3 style="margin-top:0;display:flex;align-items:center;gap:8px">🦠 Антивирусная защита (PixelGuard AV) и сканирование накопителей</h3>
      <p style="font-size:0.95rem;color:var(--text-muted,#475569);margin-bottom:1rem">
        Автономный антивирусный сканер на базе открытых баз безопасности: автоматически проверяет подключаемые USB-накопители, выполняет экспресс- и фоновую детекцию угроз, изолирует подозрительные файлы в карантин с шифрованием и контролирует нагрузку на систему.
      </p>

      <label class="check-label" style="font-size:1.05rem;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:8px;margin-bottom:1.25rem">
        <input id="av-enabled" type="checkbox" ${s.antivirusEnabled !== false ? 'checked' : ''}>
        Включить антивирусную защиту PixelGuard AV на всех ПК
      </label>

      <!-- Информационный блок источников баз -->
      <div style="background:var(--bg-secondary,#F8FAFC);border:1px solid var(--border,#E2E8F0);border-radius:8px;padding:1rem;margin-bottom:1.25rem">
        <h4 style="margin:0 0 0.5rem 0;font-size:0.95rem">Источники открытых баз безопасности:</h4>
        <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(220px, 1fr));gap:0.75rem;font-size:0.85rem">
          <div style="background:#fff;border:1px solid #CBD5E1;border-radius:6px;padding:0.75rem">
            <strong>Cisco Talos ClamAV</strong><br>
            <span style="color:var(--text-muted,#64748B)">PE-файлы, архивы, байткод</span><br>
            <span class="badge" style="margin-top:4px;display:inline-block;background:#E0F2FE;color:#0369A1">${manifest.clamAvVersion || 'Подключено'}</span>
          </div>
          <div style="background:#fff;border:1px solid #CBD5E1;border-radius:6px;padding:0.75rem">
            <strong>YARA Community</strong><br>
            <span style="color:var(--text-muted,#64748B)">Autorun-черви, макросы, скрипты</span><br>
            <span class="badge" style="margin-top:4px;display:inline-block;background:#DCFCE7;color:#15803D">${manifest.yaraRulesCount ? manifest.yaraRulesCount + ' правил' : 'Активно'}</span>
          </div>
          <div style="background:#fff;border:1px solid #CBD5E1;border-radius:6px;padding:0.75rem">
            <strong>Abuse.ch MalwareBazaar</strong><br>
            <span style="color:var(--text-muted,#64748B)">SHA-256 свежих угроз за 30 дней</span><br>
            <span class="badge" style="margin-top:4px;display:inline-block;background:#FEF3C7;color:#B45309">${manifest.malwareBazaarHashCount ? manifest.malwareBazaarHashCount.toLocaleString() + ' хэшей' : 'Синхронизировано'}</span>
          </div>
        </div>
        <div style="margin-top:0.75rem;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:0.5rem">
          <span style="font-size:0.85rem;color:var(--text-muted,#64748B)">
            Последнее обновление баз: <strong>${av.lastSyncAt ? new Date(av.lastSyncAt).toLocaleString() : 'Только что'}</strong>
            ${manifest.bundleSizeBytes ? ` · Размер пакета: ${(manifest.bundleSizeBytes / 1024 / 1024).toFixed(2)} МБ` : ''}
          </span>
          <button id="btn-sync-av-now" type="button" class="secondary" style="font-size:0.85rem;padding:4px 10px">
            🔄 Обновить базы сейчас
          </button>
        </div>
      </div>

      <!-- Настройки расписания, карантина и адаптивной производительности -->
      <div style="display:grid;grid-template-columns:repeat(auto-fit, minmax(280px, 1fr));gap:1rem;margin-bottom:1.25rem">
        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Расписание загрузки баз сервером:</label>
          <select id="av-schedule-hours" style="width:100%;padding:6px 10px;border-radius:6px;border:1px solid var(--border,#CBD5E1)">
            <option value="3" ${s.antivirusScheduleHours === 3 ? 'selected' : ''}>Каждые 3 часа (рекомендуется для сети)</option>
            <option value="6" ${s.antivirusScheduleHours === 6 || !s.antivirusScheduleHours ? 'selected' : ''}>Каждые 6 часов (стандартный режим)</option>
            <option value="12" ${s.antivirusScheduleHours === 12 ? 'selected' : ''}>Каждые 12 часов</option>
            <option value="24" ${s.antivirusScheduleHours === 24 ? 'selected' : ''}>Раз в сутки (24 часа)</option>
          </select>
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">Сервер защищен от блокировок Cloudflare по IP-адресу.</small>
        </div>

        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Срок хранения угроз в карантине:</label>
          <select id="av-retention-days" style="width:100%;padding:6px 10px;border-radius:6px;border:1px solid var(--border,#CBD5E1)">
            <option value="7" ${s.antivirusRetentionDays === 7 ? 'selected' : ''}>7 дней</option>
            <option value="14" ${s.antivirusRetentionDays === 14 ? 'selected' : ''}>14 дней</option>
            <option value="30" ${s.antivirusRetentionDays === 30 || !s.antivirusRetentionDays ? 'selected' : ''}>30 дней (рекомендуется)</option>
            <option value="60" ${s.antivirusRetentionDays === 60 ? 'selected' : ''}>60 дней</option>
            <option value="0" ${s.antivirusRetentionDays === 0 ? 'selected' : ''}>Бессрочно (только ручное удаление)</option>
          </select>
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">По истечении срока файл автоматически физически удаляется с диска ПК.</small>
        </div>

        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Профиль нагрузки на компьютер:</label>
          <select id="av-perf-profile" style="width:100%;padding:6px 10px;border-radius:6px;border:1px solid var(--border,#CBD5E1)">
            <option value="auto" ${s.performanceProfile === 'auto' || !s.performanceProfile ? 'selected' : ''}>Авто (Адаптивный под мощность ПК)</option>
            <option value="low" ${s.performanceProfile === 'low' ? 'selected' : ''}>Энергосберегающий (CPU &lt;15%, слабые ПК)</option>
            <option value="balanced" ${s.performanceProfile === 'balanced' ? 'selected' : ''}>Сбалансированный (CPU &lt;25–30%)</option>
            <option value="high" ${s.performanceProfile === 'high' ? 'selected' : ''}>Максимальный (для свободных станций)</option>
          </select>
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">Помощник определяет CPU/RAM и не нагружает систему на 100%.</small>
        </div>

        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Лимит нагрузки процессора:</label>
          <div style="display:flex;align-items:center;gap:10px">
            <input id="av-max-cpu" type="range" min="10" max="50" step="5" value="${s.maxCpuPercent || 25}" style="flex:1">
            <span id="av-max-cpu-val" style="font-weight:600;min-width:45px">${s.maxCpuPercent || 25}%</span>
          </div>
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">Пользователь может комфортно работать в браузере и 1С.</small>
        </div>
      </div>

      <div style="margin-bottom:1.25rem">
        <label class="check-label" style="font-size:0.95rem;cursor:pointer;display:inline-flex;align-items:center;gap:8px">
          <input id="av-pause-user-activity" type="checkbox" ${s.pauseOnUserActivity !== false ? 'checked' : ''}>
          Приостанавливать фоновое сканирование при активной работе пользователя (User Activity Backoff)
        </label>
      </div>

      <!-- Исключения от сканирования -->
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:1rem;margin-bottom:1.25rem">
        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Исключаемые расширения файлов:</label>
          <input id="av-excluded-ext" type="text" value="${escape(s.excludedExtensions || '.iso, .vmdk, .bak, .1cd, .log')}" style="width:100%;padding:6px 10px;border-radius:6px;border:1px solid var(--border,#CBD5E1)">
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">Файлы с указанными расширениями пропускаются при сканировании.</small>
        </div>
        <div>
          <label style="display:block;font-weight:600;margin-bottom:0.35rem;font-size:0.9rem">Исключаемые пути и папки:</label>
          <input id="av-excluded-paths" type="text" placeholder="C:\\Bases\\1C; D:\\Backups" value="${escape(s.excludedPaths || '')}" style="width:100%;padding:6px 10px;border-radius:6px;border:1px solid var(--border,#CBD5E1)">
          <small style="color:var(--text-muted,#64748B);font-size:0.8rem">Разделитель путей — точка с запятой (;).</small>
        </div>
      </div>

      <!-- Доверие Windows Defender GPO -->
      <div style="border-top:1px solid var(--border,#E2E8F0);padding-top:1rem;margin-top:1rem;display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:1rem">
        <div>
          <strong>Политика доверия Windows Defender (GPO):</strong><br>
          <span style="font-size:0.85rem;color:var(--text-muted,#64748B)">Исключает PixelHelper и его антивирусные базы из проверок защитника Windows Defender во избежание конфликтов.</span>
        </div>
        <a href="/api/settings/security/antivirus-gpo-script" download="Deploy-AntivirusTrustGpo.ps1" class="secondary" style="display:inline-flex;align-items:center;gap:6px;text-decoration:none;padding:6px 12px;border-radius:6px;font-size:0.9rem">
          ⬇ Скачать GPO доверия Defender (.ps1)
        </a>
      </div>

      <div style="margin-top:1.25rem">
        <button id="save-security-switch" class="primary" type="button" style="padding:8px 20px;font-weight:600">
          Сохранить параметры безопасности
        </button>
        <span id="security-save-result" style="margin-left:1rem;font-size:0.9rem"></span>
      </div>
    </div>

    <!-- ЖУРНАЛ ОБНАРУЖЕННЫХ УГРОЗ И КАРАНТИН -->
    <div style="background:var(--card-bg,#fff);border:1px solid var(--border,#CBD5E1);border-radius:8px;padding:1.25rem">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:1rem;flex-wrap:wrap;gap:0.75rem">
        <div>
          <h3 style="margin:0;display:flex;align-items:center;gap:8px">☣️ Журнал обнаруженных угроз и управление карантином</h3>
          <p style="margin:4px 0 0 0;font-size:0.85rem;color:var(--text-muted,#64748B)">
            Просмотр изолированных файлов на рабочих станциях сети, полное удаление с диска или восстановление с автоматическим занесением в список исключений.
          </p>
        </div>
        <div style="display:flex;align-items:center;gap:8px">
          <select id="threat-filter-status" style="padding:5px 8px;border-radius:6px;border:1px solid var(--border,#CBD5E1);font-size:0.85rem">
            <option value="all">Все статусы</option>
            <option value="Quarantined" selected>В карантине</option>
            <option value="Restored">Восстановленные</option>
            <option value="Deleted">Удаленные</option>
          </select>
          <button id="btn-refresh-threats" class="secondary" type="button" style="font-size:0.85rem;padding:5px 10px">Обновить</button>
        </div>
      </div>

      <div class="table-wrap">
        <table style="width:100%;border-collapse:collapse;font-size:0.88rem">
          <thead>
            <tr style="border-bottom:2px solid var(--border,#E2E8F0);text-align:left">
              <th style="padding:8px">ДАТА</th>
              <th style="padding:8px">КОМПЬЮТЕР</th>
              <th style="padding:8px">ПОЛЬЗОВАТЕЛЬ</th>
              <th style="padding:8px">ИСХОДНЫЙ ПУТЬ</th>
              <th style="padding:8px">УГРОЗА</th>
              <th style="padding:8px">СТАТУС</th>
              <th style="padding:8px;text-align:right">ДЕЙСТВИЯ</th>
            </tr>
          </thead>
          <tbody id="threat-table-body">
            <tr><td colspan="7" style="padding:20px;text-align:center;color:var(--text-muted,#64748B)">Загрузка списка угроз…</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>`;

  // Slider update listener
  $('#av-max-cpu').addEventListener('input', e => {
    $('#av-max-cpu-val').textContent = e.target.value + '%';
  });

  // Load Threats
  async function loadThreats() {
    const tbody = $('#threat-table-body');
    if (!tbody) return;
    try {
      const status = $('#threat-filter-status').value;
      const threats = await api('/antivirus/threats?status=' + encodeURIComponent(status));
      if (!threats || !threats.length) {
        tbody.innerHTML = `<tr><td colspan="7" style="padding:20px;text-align:center;color:var(--text-muted,#64748B)">Обнаруженных угроз нет. Система безопасности в норме.</td></tr>`;
        return;
      }
      tbody.innerHTML = threats.map(t => {
        const isQuarantined = t.status === 'Quarantined';
        const isRestored = t.status === 'Restored';
        const isDeleted = t.status === 'Deleted';
        let statusBadge = `<span class="badge" style="background:#FEF3C7;color:#B45309">В карантине</span>`;
        if (isRestored) statusBadge = `<span class="badge" style="background:#DCFCE7;color:#15803D">Восстановлен (исключение)</span>`;
        if (isDeleted) statusBadge = `<span class="badge" style="background:#F1F5F9;color:#64748B">Удален безвозвратно</span>`;

        let retentionInfo = '';
        if (isQuarantined && t.quarantinedUntil) {
          const daysLeft = Math.ceil((new Date(t.quarantinedUntil) - new Date()) / (1000 * 3600 * 24));
          retentionInfo = daysLeft > 0 ? `<div style="font-size:0.75rem;color:var(--text-muted,#64748B)">Автоудаление через ${daysLeft} дн.</div>` : `<div style="font-size:0.75rem;color:#DC2626">Срок истёк</div>`;
        }

        return `<tr style="border-bottom:1px solid var(--border,#E2E8F0)">
          <td style="padding:8px;white-space:nowrap">${new Date(t.detectedAt).toLocaleString()}</td>
          <td style="padding:8px;font-weight:600">${escape(t.machineName)}</td>
          <td style="padding:8px">${escape(t.userName || '—')}</td>
          <td style="padding:8px;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${escape(t.filePath)}">
            <code>${escape(t.filePath)}</code>
          </td>
          <td style="padding:8px">
            <strong style="color:#DC2626">${escape(t.threatName)}</strong>
            <div style="font-size:0.75rem;color:var(--text-muted,#64748B)">${escape(t.threatType)} · ${(t.fileSizeBytes / 1024).toFixed(1)} КБ</div>
          </td>
          <td style="padding:8px">
            ${statusBadge}
            ${retentionInfo}
          </td>
          <td style="padding:8px;text-align:right;white-space:nowrap">
            ${isQuarantined ? `
              <button class="secondary btn-restore-threat" data-id="${t.id}" style="color:#15803D;padding:4px 8px;font-size:0.8rem;margin-right:4px" title="Восстановить файл на ПК и добавить в исключения">
                Восстановить
              </button>
              <button class="secondary btn-delete-threat" data-id="${t.id}" style="color:#DC2626;padding:4px 8px;font-size:0.8rem" title="Удалить файл навсегда с компьютера">
                Удалить навсегда
              </button>
            ` : '—'}
          </td>
        </tr>`;
      }).join('');

      // Bind restore buttons
      tbody.querySelectorAll('.btn-restore-threat').forEach(btn => {
        btn.addEventListener('click', async () => {
          const id = btn.dataset.id;
          if (!confirm('Восстановить файл из карантина на прежнее место?\n\nФайл будет расшифрован, возвращен на рабочий компьютер и автоматически добавлен в доверенный белый список исключений, чтобы не попадать в карантин повторно.')) return;
          btn.disabled = true;
          try {
            await api(`/antivirus/threats/${id}/restore`, 'POST');
            toast('Команда на восстановление отправлена агенту. Файл добавлен в исключения.');
            loadThreats();
          } catch (e) {
            toast(e.message, true);
            btn.disabled = false;
          }
        });
      });

      // Bind delete buttons
      tbody.querySelectorAll('.btn-delete-threat').forEach(btn => {
        btn.addEventListener('click', async () => {
          const id = btn.dataset.id;
          if (!confirm('ВНИМАНИЕ! Полностью и безвозвратно стереть изолированный файл с рабочего компьютера?')) return;
          btn.disabled = true;
          try {
            await api(`/antivirus/threats/${id}`, 'DELETE');
            toast('Файл безвозвратно удален с рабочего компьютера.');
            loadThreats();
          } catch (e) {
            toast(e.message, true);
            btn.disabled = false;
          }
        });
      });

    } catch (e) {
      tbody.innerHTML = `<tr><td colspan="7" style="padding:20px;text-align:center;color:#DC2626">Ошибка загрузки угроз: ${escape(e.message)}</td></tr>`;
    }
  }

  $('#btn-refresh-threats').addEventListener('click', loadThreats);
  $('#threat-filter-status').addEventListener('change', loadThreats);
  loadThreats();

  // Manual AV sync now
  $('#btn-sync-av-now').addEventListener('click', async () => {
    const btn = $('#btn-sync-av-now');
    btn.disabled = true;
    btn.textContent = '⏳ Синхронизация…';
    try {
      const res = await api('/antivirus/sync-now', 'POST');
      toast(res.message || 'Синхронизация баз успешно запущена');
      // Reload page data
      const refreshed = await api('/settings/security');
      renderSecuritySettings(refreshed);
    } catch (e) {
      toast(e.message, true);
      btn.disabled = false;
      btn.textContent = '🔄 Обновить базы сейчас';
    }
  });

  // Save all security & AV settings
  $('#save-security-switch').addEventListener('click', async () => {
    const btn = $('#save-security-switch');
    const result = $('#security-save-result');
    btn.disabled = true;
    result.textContent = 'Сохранение…';
    try {
      const payload = {
        webGuardEnabled: $('#webguard-enabled').checked,
        antivirusEnabled: $('#av-enabled').checked,
        antivirusScheduleHours: parseInt($('#av-schedule-hours').value, 10) || 6,
        antivirusRetentionDays: parseInt($('#av-retention-days').value, 10),
        performanceProfile: $('#av-perf-profile').value,
        maxCpuPercent: parseInt($('#av-max-cpu').value, 10) || 25,
        pauseOnUserActivity: $('#av-pause-user-activity').checked,
        excludedExtensions: $('#av-excluded-ext').value,
        excludedPaths: $('#av-excluded-paths').value
      };
      await api('/settings/security', 'PUT', payload);
      result.textContent = 'Параметры безопасности сохранены и отправлены на рабочие станции.';
      result.style.color = 'var(--success,#16a34a)';
      toast('Настройки безопасности и антивируса сохранены');
    } catch (e) {
      result.textContent = e.message;
      result.style.color = 'var(--error,#dc2626)';
      toast(e.message, true);
    } finally {
      btn.disabled = false;
    }
  });
}

function renderBranches(items){const host=($('#settings-tab-body')||$('#content'));host.innerHTML='<div class="toolbar"><button id="add-branch" class="primary">＋ Добавить филиал</button><button id="sync-branches-btn" class="secondary" title="Автоматически назначить филиалы ПК и пользователям">🔄 Синхронизировать филиалы ПК и пользователей</button></div>'+table(['ФИЛИАЛ','ПРЕФИКСЫ ПК','IP-ПОДСЕТИ','СТАТУС','ДЕЙСТВИЯ'],items.map(b=>`<tr><td><strong>${escape(b.name)}</strong></td><td><code>${escape(b.computerPrefixes||'—')}</code></td><td><code>${escape(b.ipSubnets||'—')}</code></td><td>${b.isActive?'Активен':'Отключён'}</td><td><button data-edit-branch="${b.id}">Изменить</button> <button data-delete-branch="${b.id}">Удалить</button></td></tr>`).join(''));$('#add-branch').addEventListener('click',()=>branchForm());$('#sync-branches-btn').addEventListener('click',async()=>{try{const res=await api('/branches/sync','POST',{});toast(`Синхронизировано ПК: ${res.updatedComputers}, пользователей: ${res.updatedUsers}`);await loadPage();}catch(e){toast(e.message,true);}});host.querySelectorAll('[data-edit-branch]').forEach(b=>b.addEventListener('click',()=>branchForm(items.find(i=>i.id===Number(b.dataset.editBranch)))));host.querySelectorAll('[data-delete-branch]').forEach(b=>b.addEventListener('click',()=>remove('/branches/'+b.dataset.deleteBranch,'Удалить филиал? Используемый филиал можно только отключить.')));}
function branchForm(branch={name:'',computerPrefixes:'',ipSubnets:'',isActive:true}){modal(branch.id?'Изменить филиал':'Новый филиал',`<form id="branch-form"><label>Название<input name="name" maxlength="100" value="${escape(branch.name)}" required></label><label>Префиксы ПК (первые буквы имени компьютера через запятую или пробел)<input name="computerPrefixes" maxlength="200" value="${escape(branch.computerPrefixes||'')}" placeholder="N, R, D, B"></label><small>По первой букве имени ПК определяются филиал и экстренные вызовы (N — Поликлиника, R — Роддом, D — Детская больница, B — Больница и любые другие буквы).</small><label>IP-подсети филиала (через запятую)<input name="ipSubnets" maxlength="300" value="${escape(branch.ipSubnets||'')}" placeholder="172.16.10.0/24, 10.20.1.*"></label><small>Если у филиалов совпадают начальные буквы ПК, точное сопоставление выполняется по IP-адресу.</small><label class="check-label"><input name="isActive" type="checkbox" ${branch.isActive?'checked':''}>Активен</label><small>Отключённый филиал недоступен для новых заявок. История сохраняется.</small><div id="form-error" class="error"></div><button class="primary">Сохранить</button></form>`);$('#branch-form').addEventListener('submit',async e=>{e.preventDefault();try{await api('/branches'+(branch.id?'/'+branch.id:''),branch.id?'PUT':'POST',{name:e.target.elements.name.value,computerPrefixes:e.target.elements.computerPrefixes.value,ipSubnets:e.target.elements.ipSubnets.value,isActive:e.target.elements.isActive.checked});closeModal();await loadPage();}catch(error){$('#form-error').textContent=error.message;}});decorateSettingsHelp($('#branch-form'),'branches');}

function renderMessengerSettings(data){
  ($('#settings-tab-body')||$('#content')).innerHTML=`<form id="messenger-settings" class="settings-card"><h2>Мессенджер сотрудников</h2><label class="check"><input name="enabled" type="checkbox" ${data.enabled?'checked':''}> Включить личную переписку</label><label>Срок хранения сообщений, дней<input name="retentionDays" type="number" min="0" max="3650" value="${Number(data.retentionDays)||0}" required></label><p>0 — без ограничения; иначе от 30 до 3650 дней. Сообщения старше указанного срока удаляются при ежедневной очистке.</p><label>Интервал напоминания о непрочитанных сообщениях (минут)<input name="unreadReminderMinutes" type="number" min="1" max="1440" value="${Number(data.unreadReminderMinutes)||5}" required></label><p>Помощник периодически привлекает внимание пользователя и напоминает о непрочитанных сообщениях, пока они не будут прочитаны.</p><p>Сохранено сообщений: ${Number(data.storedMessages)||0}. Содержание переписки доступно только её участникам. Администраторы не получают доступ к чужим диалогам.</p><p>Сотрудник входит через AD в окне клиента. В списке отображаются AD-пользователи панели, включая вошедших из мессенджера. Обновите клиент до 1.2.11 или новее.</p><div id="form-error" class="error"></div><button class="primary">Сохранить</button></form>`;
  $('#messenger-settings').addEventListener('submit',async e=>{e.preventDefault();const retentionDays=Number(e.target.retentionDays.value);const unreadReminderMinutes=Number(e.target.unreadReminderMinutes.value);if(retentionDays!==0&&(retentionDays<30||retentionDays>3650)){$('#form-error').textContent='Укажите 0 либо от 30 до 3650 дней.';return;}if(unreadReminderMinutes<1||unreadReminderMinutes>1440){$('#form-error').textContent='Интервал напоминания должен быть от 1 до 1440 минут.';return;}if(retentionDays!==0&&retentionDays!==data.retentionDays&&!confirm('Сообщения старше '+retentionDays+' дней будут удаляться. Сохранить срок хранения?'))return;try{await api('/settings/messenger','PUT',{enabled:e.target.enabled.checked,retentionDays,unreadReminderMinutes});toast('Настройки мессенджера сохранены');await loadPage();}catch(error){$('#form-error').textContent=error.message;}});
  decorateSettingsHelp($('#messenger-settings'),'messenger');
}

function renderCartridgeSettings(data){
  ($('#settings-tab-body')||$('#content')).innerHTML=`<div class="panel glpi-settings-panel">
    <h2>Интеграция с системой учёта картриджей</h2>
    <p>Позволяет автоматически отправлять всплывающие уведомления на рабочие станции сотрудников, когда сданные ими картриджи заправлены и готовы к выдаче в IT-отделе.</p>
    <form id="cartridge-form">
      <label class="check-label"><input name="enabled" type="checkbox" ${data.enabled?'checked':''}>Включить оповещения через интерактивного помощника</label>
      <label>Секретный API-ключ интеграции (опционально)<input name="apiKey" value="${escape(data.apiKey||'')}" placeholder="Произвольный секретный ключ" maxlength="200"></label>
      <div class="form-actions">
        <button class="primary" type="submit">Сохранить настройки</button>
        <button id="cartridge-test-btn" type="button">Отправить тестовое оповещение</button>
      </div>
      <p id="cartridge-result" role="status"></p>
    </form>
    <div style="margin-top:1.5rem;padding:1rem;background:#f8fafc;border-radius:8px;border:1px solid #e2e8f0">
      <h4>Инструкция по настройке в сервисе «Учёт картриджей» (сервер ithub):</h4>
      <p style="font-size:0.9rem;color:#64748b">
        1. В веб-интерфейсе учёта картриджей перейдите во вкладку <strong>«Интерактивный помощник»</strong>.<br>
        2. Укажите адрес сервера: <code>${window.location.origin}</code><br>
        3. Если настроен API-ключ, укажите его в поле токена.<br>
        4. Эндпоинт приёма вебхуков: <code>${window.location.origin}/api/integrations/cartridges/ready</code>.
      </p>
    </div>
  </div>`;
  const form=$('#cartridge-form'), result=$('#cartridge-result');
  form.addEventListener('submit',async e=>{
    e.preventDefault();
    try{
      await api('/settings/cartridge','PUT',{enabled:form.elements.enabled.checked,apiKey:form.elements.apiKey.value.trim()});
      result.textContent='Настройки сохранены';result.className='';toast('Настройки сохранены');
    }catch(err){result.textContent=err.message;result.className='error';}
  });
  $('#cartridge-test-btn').addEventListener('click',async()=>{
    try{
      await api('/integrations/cartridges/ready','POST',[{
        username: me.username,
        marker: 'TEST-001',
        model: 'Canon 725 / HP 85A',
        cabinet: 'IT Кабинет',
        itOffice: 'Кабинет IT № 108',
        message: 'Тестовое уведомление: ваш картридж заправлен и ожидает выдачи!'
      }]);
      result.textContent='Тестовое уведомление отправлено на ваш ПК';result.className='';toast('Тестовое уведомление отправлено');
    }catch(err){result.textContent=err.message;result.className='error';}
  });
  decorateSettingsHelp($('#cartridge-form'),'cartridge');
}

function renderEmergencySettings(data, branches=[], recipients={users:[],groups:[]}){
  if (!data.codeBlueBranchUsers) data.codeBlueBranchUsers = {};
  let depts = [...(data.departments||[])];
  depts.forEach(d => { if (!d.branchResponsibleUsers) d.branchResponsibleUsers = {}; });

  const activeBranches = (branches || []).filter(b => b.isActive !== false);
  const userMap = new Map((recipients.users || []).map(u => [u.value.toLowerCase(), u]));
  const getUserLabel = (login) => {
    const u = userMap.get((login||'').toLowerCase());
    return u ? (u.label ? `${u.label} (${u.login})` : u.login) : login;
  };

  const render=()=>{
    const host=($('#settings-tab-body')||$('#content'));
    host.innerHTML=`<div class="panel emergency-settings-panel">
      <h2>Система экстренного оповещения (AudioRONGTA / ROXTON)</h2>
      <p>Интеграция с системой голосового и визуального оповещения ROXTON и службой вызова медицинских специалистов.</p>
      <form id="emergency-form">
        <div style="padding:1rem;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;margin-bottom:1rem">
          <h3 style="margin-top:0;font-size:1.05rem;color:#1e293b;display:flex;align-items:center;gap:6px">
            <span>📡</span> Шлюз связи с сервером AudioRONGTA
          </h3>
          <label class="check-label" style="font-weight:600"><input name="enabled" type="checkbox" ${data.enabled?'checked':''}>Включить интеграцию с сервером AudioRONGTA</label>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:0.75rem;margin-top:0.5rem">
            <label>Адрес сервера экстренного оповещения<input name="serverUrl" value="${escape(data.serverUrl||'http://172.16.16.63:8085')}" placeholder="http://172.16.16.63:8085" maxlength="255"></label>
            <label>Секретный ключ / X-Client-Key (опционально)<input name="apiKey" value="${escape(data.apiKey||'')}" placeholder="Оставьте пустым для авто-авторизации моста или укажите мастер-ключ" maxlength="255"></label>
          </div>
          <small style="color:#64748b;display:block;margin-top:0.25rem;margin-bottom:0.75rem">На сервере AudioRONGTA используется токен рабочего места. Если поле пустое, Little Helper автоматически использует зарегистрированный авторизованный токен моста.</small>
          <div style="display:flex;flex-wrap:wrap;gap:1rem;margin-top:0.5rem">
            <label class="check-label"><input name="allowStandalone" type="checkbox" ${data.allowStandalone!==false?'checked':''}>Автономный режим (оповещения работают напрямую через помощников при недоступности AudioRONGTA)</label>
            <label class="check-label"><input name="allowClientTrigger" type="checkbox" ${data.allowClientTrigger!==false?'checked':''}>Разрешить сотрудникам инициировать тревогу из трея помощника</label>
          </div>
          <div style="margin-top:0.75rem">
            <button type="button" id="emergency-sync-all" class="secondary" style="font-size:0.85rem">🔄 Первоначальная синхронизация отделений и компьютеров с AudioRONGTA</button>
          </div>
        </div>

        <div style="padding:1rem;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;margin-bottom:1rem">
          <label class="check-label" style="font-weight:600;margin-bottom:0.5rem"><input name="broadcastAllBranches" type="checkbox" ${data.broadcastAllBranches?'checked':''}>Массовые коды во все филиалы (без ограничения текущим корпусом)</label>
          ${emergencyCodeMatrix(data, activeBranches, getUserLabel)}
        </div>
        
        <div style="padding:1rem;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;margin-bottom:1rem">
          <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.75rem;margin-bottom:0.75rem">
            <div>
              <h3 style="margin:0 0 0.25rem 0;font-size:1.05rem;color:#1e293b;display:flex;align-items:center;gap:6px">
                <span>🩺</span> Служба вызова медицинских специалистов
              </h3>
              <p style="margin:0;font-size:0.85rem;color:#64748b">Матрица филиалов и отделений. Назначенные специалисты получают тревожное окно вызова на своих рабочих станциях.</p>
            </div>
            <div style="display:flex;flex-wrap:wrap;gap:0.5rem">
              <button type="button" id="btn-add-specialist-user" class="primary" style="font-size:0.85rem">＋ Добавить сотрудника в филиал и отделение</button>
              <button type="button" id="btn-edit-depts" class="secondary" style="font-size:0.85rem">⚙️ Редактировать отделения</button>
            </div>
          </div>
          ${renderSpecialistMatrix(depts, activeBranches, getUserLabel)}
        </div>

        <div style="margin-top:1rem;margin-bottom:1rem;padding:0.75rem;background:#f0fdf4;border:1px solid #bbf7d0;border-radius:6px">
          <label class="check-label" style="font-weight:600;color:#166534">
            <input name="telegramAlertsEnabled" type="checkbox" ${data.telegramAlertsEnabled!==false?'checked':''}>Дублировать экстренные коды и вызовы «Код Синий» в Telegram
          </label>
          <div style="display:grid;grid-template-columns:2fr 1fr;gap:0.75rem;margin-top:0.5rem">
            <label style="font-size:0.85rem">Chat ID для оповещений (по умолчанию чат техподдержки)
              <input name="telegramChatId" value="${escape(data.telegramChatId||'')}" placeholder="Оставьте пустым для основного чата" maxlength="64">
            </label>
            <label style="font-size:0.85rem">Thread ID темы (опционально)
              <input name="telegramThreadId" type="number" value="${data.telegramThreadId||''}" placeholder="0" min="0">
            </label>
          </div>
          <small style="color:#15803d">Экстренные сигналы будут моментально приходить дежурным инженерам и руководству в Telegram со звуковым уведомлением и деталями (филиал, кабинет, инициатор, время).</small>
        </div>

        <div class="form-actions" style="display:flex;flex-wrap:wrap;gap:0.5rem">
          <button class="primary" type="submit">Сохранить настройки</button>
          <button id="emergency-ping-btn" type="button" class="secondary">🔍 Проверить связь с AudioRONGTA</button>
          <button id="emergency-test-btn" type="button">Тестовое оповещение на все ПК</button>
          <button id="emergency-cancel-btn" type="button" class="danger" style="background:#059669;border-color:#047857;color:#fff;font-weight:600">🟢 Сбросить экстренное оповещение (Отбой тревоги)</button>
        </div>
        <p id="emergency-result" role="status"></p>
      </form>

      <div style="margin-top:2rem;padding:1.25rem;background:#fef2f2;border:1px solid #fecaca;border-radius:10px">
        <h3 style="color:#b91c1c;margin-top:0">🚨 Ручной запуск экстренного оповещения</h3>
        <p style="font-size:0.85rem;color:#7f1d1d">Запуск оповещения на всех экранах рабочих станций медицинского учреждения.</p>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:0.75rem;margin-bottom:0.75rem">
          <label>Код тревоги
            <select id="manual-alert-code">
              <option value="CODE_RED">🔥 КОД КРАСНЫЙ (Пожар / Эвакуация)</option>
              <option value="CODE_BLACK">💣 КОД ЧЁРНЫЙ (Теракт / Взрывное устройство)</option>
              <option value="CODE_ORANGE">☣️ КОД ОРАНЖЕВЫЙ (Опасные вещества / ЧС)</option>
              <option value="CODE_YELLOW">⚠️ КОД ЖЁЛТЫЙ (Чрезвычайная ситуация)</option>
              <option value="CODE_BLUE">💙 КОД СИНИЙ (Остановка сердца / Реанимация)</option>
              <option value="CODE_WHITE">🛡️ КОД БЕЛЫЙ (Нападение / Агрессия)</option>
              <option value="CODE_PINK">🚸 КОД РОЗОВЫЙ (Потерян ребёнок)</option>
            </select>
          </label>
          <label>Кабинет / Место происшествия
            <select id="manual-alert-branch"><option value="">Выберите филиал</option></select><input id="manual-alert-cabinet" placeholder="Например: Кабинет 204">
          </label>
        </div>
        <label>Примечание / Инструкция
          <textarea id="manual-alert-notes" rows="2" placeholder="Дополнительная информация (будет выведена на экранах)"></textarea>
        </label>
        <div style="display:flex;gap:0.75rem;margin-top:0.75rem">
          <button id="manual-alert-trigger" class="danger" style="font-weight:bold">Запустить экстренное оповещение</button>
          <button id="manual-alert-cancel" type="button" class="danger" style="background:#059669;border-color:#047857;color:#fff;font-weight:bold">🟢 Отбой тревоги</button>
        </div>
      </div>

      <div style="margin-top:1.5rem;padding:1.25rem;background:#0f172a;border:1px solid #334155;border-radius:10px;color:#f8fafc">
        <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.75rem">
          <div>
            <h3 style="color:#ffffff;margin:0;display:flex;align-items:center;gap:0.5rem">
              <span>🛡️</span> Экран поста охраны «Код Белый» (Тихий вызов)
            </h3>
            <p style="font-size:0.85rem;color:#94a3b8;margin:0.25rem 0 0 0">
              Выделенная веб-страница для монитора охраны. По умолчанию чёрный экран, при тихом вызове загорается ослепительно белым с номером кабинета на 10 минут.
            </p>
          </div>
          <div style="display:flex;gap:0.5rem">
            <a id="open-guard-white-btn" href="/guard-white.html" target="_blank" class="button primary" style="background:#2563eb;color:#fff;text-decoration:none;padding:8px 14px;border-radius:6px;font-weight:600;display:inline-flex;align-items:center;gap:6px">
              Открыть экран охраны ↗
            </a>
            <button type="button" id="copy-guard-white-btn" class="secondary" style="background:#334155;color:#fff;border-color:#475569;padding:8px 14px;border-radius:6px">
              Копировать ссылку 📋
            </button>
          </div>
        </div>
        <div style="margin-top:0.75rem;padding:0.5rem 0.75rem;background:#1e293b;border-radius:6px;font-family:monospace;font-size:0.85rem;color:#38bdf8;word-break:break-all;display:flex;justify-content:space-between;align-items:center">
          <span id="guard-white-url-text">${window.location.origin}/guard-white.html</span>
        </div>
      </div>
    </div>`;

    // Bind removal buttons:
    host.querySelectorAll('[data-remove-blue-user]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        const [bId, login] = btn.dataset.removeBlueUser.split(':');
        if (data.codeBlueBranchUsers?.[bId]) {
          data.codeBlueBranchUsers[bId] = data.codeBlueBranchUsers[bId].filter(u => u.toLowerCase() !== login.toLowerCase());
          render();
        }
      });
    });

    host.querySelectorAll('[data-remove-dept-user]').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        const parts = btn.dataset.removeDeptUser.split(':');
        const deptId = parts[0], bId = parts[1], login = parts.slice(2).join(':');
        const dept = depts.find(d => String(d.id) === String(deptId));
        if (dept?.branchResponsibleUsers?.[bId]) {
          dept.branchResponsibleUsers[bId] = dept.branchResponsibleUsers[bId].filter(u => u.toLowerCase() !== login.toLowerCase());
          render();
        }
      });
    });

    // Code blue add user modal:
    const btnAddBlue = $('#btn-add-code-blue-user');
    if (btnAddBlue) {
      btnAddBlue.addEventListener('click', () => {
        openAddCodeBlueModal(data, activeBranches, recipients, getUserLabel, () => render());
      });
    }

    // Specialist add user modal:
    const btnAddSpec = $('#btn-add-specialist-user');
    if (btnAddSpec) {
      btnAddSpec.addEventListener('click', () => {
        if (!depts.length) { toast('Сначала создайте отделения в разделе «Редактировать отделения»', true); return; }
        openAddSpecialistModal(depts, activeBranches, recipients, getUserLabel, () => render());
      });
    }

    // Edit departments modal:
    const btnEditDepts = $('#btn-edit-depts');
    if (btnEditDepts) {
      btnEditDepts.addEventListener('click', () => {
        openEditDepartmentsModal(depts, () => render());
      });
    }

    const form=$('#emergency-form'), result=$('#emergency-result');

    form.addEventListener('submit',async e=>{
      e.preventDefault();
      try{
        const update={
          enabled:form.elements.enabled.checked,
          serverUrl:form.elements.serverUrl.value.trim(),
          apiKey:form.elements.apiKey.value.trim(),
          allowStandalone:form.elements.allowStandalone.checked,
          allowClientTrigger:form.elements.allowClientTrigger.checked,
          codeBlueBranchUsers:data.codeBlueBranchUsers||{},
          codeBlueResponsibleUsers:Object.values(data.codeBlueBranchUsers||{}).flat(),
          codeBlueResponsibleGroups:data.codeBlueResponsibleGroups||[],
          broadcastAllBranches:form.elements.broadcastAllBranches.checked,
          allowedCodes:[...form.querySelectorAll('[data-code-enabled]:checked')].map(c=>c.dataset.codeEnabled).concat(['SPECIALIST_CALL']),
          codeLaunchRoles:Object.fromEntries(emergencyCodes.map(code=>[code,[...form.querySelectorAll('[data-code-role="'+code+'"]:checked')].map(c=>c.value)])),
          emergencyCodeLanguages:Object.fromEntries(emergencyCodes.map(code=>[code,[...form.querySelectorAll('[data-code-language="'+code+'"]:checked')].map(c=>c.value)])),
          telegramAlertsEnabled:form.elements.telegramAlertsEnabled?form.elements.telegramAlertsEnabled.checked:true,
          telegramChatId:form.elements.telegramChatId?form.elements.telegramChatId.value.trim():'',
          telegramThreadId:form.elements.telegramThreadId&&form.elements.telegramThreadId.value?parseInt(form.elements.telegramThreadId.value,10)||0:0,
          departments:depts.map(d=>({
            ...d,
            branchResponsibleUsers:d.branchResponsibleUsers||{},
            responsibleUsers:Object.values(d.branchResponsibleUsers||{}).flat()
          }))
        };
        await api('/settings/emergency','PUT',update);
        data=update;
        result.textContent='Настройки экстренного оповещения сохранены';result.className='';toast('Настройки сохранены');
      }catch(err){result.textContent=err.message;result.className='error';}
    });

    $('#emergency-sync-all').addEventListener('click',async()=>{
        result.textContent='Синхронизация…';
        try{const value=await api('/settings/emergency/sync-all','POST',{});result.textContent='Синхронизировано рабочих мест: '+value.clients;}
        catch(error){result.textContent=error.message;result.className='error';}
    });
    $('#emergency-ping-btn').addEventListener('click',async()=>{
      result.textContent='Проверка связи с сервером AudioRONGTA…';result.className='';
      try{
        const r=await api('/settings/emergency/ping','POST',{});
        result.textContent=r.message;result.className=r.ok?'':'error';toast(r.message,!r.ok);
      }catch(err){result.textContent=err.message;result.className='error';toast(err.message,true);}
    });

    $('#emergency-test-btn').addEventListener('click',async()=>{
      try{
        const r=await api('/settings/emergency/test','POST',{});
        result.textContent=r.message;result.className='';toast(r.message);
      }catch(err){result.textContent=err.message;result.className='error';}
    });

    const triggerCancel=async()=>{
      if(!confirm('Вы уверены, что хотите отправить сигнал ОТБОЙ ТРЕВОГИ и сбросить активные экстренные оповещения на всех ПК?')) return;
      try{
        const r=await api('/settings/emergency/cancel','POST',{});
        const deliveries=await api('/emergency/deliveries');
        if(deliveries.some(d=>d.externalState==='CancelPending')) r.message+=' Внешняя система ещё не подтвердила отбой.';
        result.textContent=r.message;result.className='';toast(r.message);
      }catch(err){result.textContent=err.message;result.className='error';toast(err.message,true);}
    };

    $('#emergency-cancel-btn').addEventListener('click',triggerCancel);
    $('#manual-alert-cancel').addEventListener('click',triggerCancel);

    const copyGuardBtn = $('#copy-guard-white-btn');
    if (copyGuardBtn) {
      copyGuardBtn.addEventListener('click', () => {
        const url = window.location.origin + '/guard-white.html';
        navigator.clipboard.writeText(url).then(() => {
          toast('Ссылка на экран охраны скопирована в буфер обмена!');
        }).catch(() => {
          toast('Ссылка: ' + url);
        });
      });
    }

    api('/branches').then(branchesList=>{const select=$('#manual-alert-branch');if(select)select.innerHTML='<option value="">Выберите филиал</option>'+branchesList.filter(b=>b.isActive).map(b=>'<option value="'+b.id+'">'+escape(b.name)+'</option>').join('');}).catch(()=>{});
    $('#manual-alert-trigger').addEventListener('click',async()=>{
      const code=$('#manual-alert-code').value;
      const cab=$('#manual-alert-cabinet').value.trim();
      const notes=$('#manual-alert-notes').value.trim();
      const branchId=Number($('#manual-alert-branch').value)||null;
      if(!branchId && !data.broadcastAllBranches){toast('Выберите филиал для оповещения',true);return;}
      if(!confirm('ВНИМАНИЕ! Вы подтверждаете немедленный запуск экстренного кода '+code+' для выбранных получателей?')) return;
      try{
        await api('/integrations/emergency/alert','POST',{code,cabinet:cab,notes,branchId});
        toast('Экстренное оповещение запущено!');
      }catch(err){toast(err.message,true);}
    });
    decorateSettingsHelp(host,'emergency');
  };
  render();
}

function renderSpecialistMatrix(depts, activeBranches, getUserLabel) {
  if (!depts.length) {
    return `<div class="empty" style="padding:1.5rem;background:#fff;border-radius:8px;border:1px dashed #cbd5e1;text-align:center">
      Отделения не настроены. Нажмите <strong>«Редактировать отделения»</strong>, чтобы добавить отделения или выполнить синхронизацию с AudioRONGTA.
    </div>`;
  }
  const minTableWidth = Math.max(800, 180 + depts.length * 200);
  return `<div style="overflow-x:auto;width:100%;border:1px solid #e2e8f0;border-radius:8px;background:#fff;box-sizing:border-box">
    <table class="table" style="width:100%;min-width:${minTableWidth}px;font-size:0.85rem;border-collapse:collapse;background:#fff">
      <thead>
        <tr style="background:#f1f5f9;color:#475569;text-align:left;border-bottom:2px solid #e2e8f0">
          <th style="padding:10px 12px;min-width:160px;font-weight:700;white-space:nowrap">Филиал</th>
          ${depts.map((d, idx) => `
            <th style="padding:10px 12px;min-width:180px;border-left:1px solid #e2e8f0;white-space:nowrap">
              <div style="display:flex;align-items:center;gap:6px">
                <span style="display:inline-block;width:12px;height:12px;border-radius:50%;background:${escape(d.color||'#2563eb')};flex-shrink:0"></span>
                <strong style="color:#1e293b">${escape(d.name || ('Отделение #' + (d.id || idx+1)))}</strong>
              </div>
              <small style="color:#64748b;font-size:0.75rem;display:block">${escape(d.specialistTitle || 'Специалист')}</small>
            </th>
          `).join('')}
        </tr>
      </thead>
      <tbody>
        ${activeBranches.map(branch => `
          <tr style="border-bottom:1px solid #f1f5f9">
            <td style="padding:10px 12px;vertical-align:top;font-weight:600;color:#334155;background:#f8fafc;white-space:nowrap">
              🏢 ${escape(branch.name)}
            </td>
            ${depts.map(d => {
              const uList = d.branchResponsibleUsers?.[branch.id] || [];
              return `<td style="padding:10px 12px;vertical-align:top;border-left:1px solid #f1f5f9">
                <div style="display:flex;flex-wrap:wrap;gap:4px">
                  ${uList.map(login => `
                    <span class="badge" style="background:#f0fdf4;color:#166534;border:1px solid #bbf7d0;border-radius:4px;padding:2px 6px;font-size:0.75rem;display:inline-flex;align-items:center;gap:4px;white-space:nowrap">
                      👤 ${escape(getUserLabel(login))}
                      <button type="button" data-remove-dept-user="${d.id}:${branch.id}:${escape(login)}" style="border:none;background:none;color:#dc2626;cursor:pointer;padding:0 2px;font-weight:bold;line-height:1" title="Удалить">✕</button>
                    </span>
                  `).join('') || '<span style="color:#94a3b8;font-size:0.75rem">—</span>'}
                </div>
              </td>`;
            }).join('')}
          </tr>
        `).join('')}
      </tbody>
    </table>
  </div>`;
}

function openAddCodeBlueModal(data, activeBranches, recipients, getUserLabel, onUpdate) {
  const users = recipients.users || [];
  modal('Добавить сотрудника в филиал · Код Синий (СЛР)', `
    <form id="modal-blue-form">
      <label style="margin-bottom:0.75rem;display:block">
        <strong>Выберите филиал:</strong>
        <select id="modal-blue-branch-select" style="width:100%;margin-top:4px" required>
          ${activeBranches.map(b => `<option value="${b.id}">${escape(b.name)}</option>`).join('')}
        </select>
      </label>
      <label style="margin-bottom:0.5rem;display:block">
        <strong>Поиск сотрудника:</strong>
        <input type="search" id="modal-blue-search-input" placeholder="Поиск по ФИО, логину, кабинету..." style="width:100%;margin-top:4px">
      </label>
      <div id="modal-blue-user-results" style="max-height:220px;overflow-y:auto;border:1px solid #cbd5e1;border-radius:6px;background:#fff;margin-bottom:1rem">
      </div>
      <div style="display:flex;justify-content:flex-end;gap:0.5rem">
        <button type="button" id="modal-blue-cancel" class="secondary">Закрыть</button>
      </div>
    </form>
  `);

  const branchSelect = $('#modal-blue-branch-select');
  const searchInput = $('#modal-blue-search-input');
  const resultsDiv = $('#modal-blue-user-results');
  $('#modal-blue-cancel').addEventListener('click', closeModal);

  const refreshList = () => {
    const q = (searchInput.value || '').trim().toLowerCase();
    const branchId = branchSelect.value;
    const currentBranchUsers = new Set((data.codeBlueBranchUsers?.[branchId] || []).map(u => u.toLowerCase()));

    const filtered = users.filter(u => {
      return (u.label || '').toLowerCase().includes(q) || (u.login || '').toLowerCase().includes(q) || (u.room || '').toLowerCase().includes(q);
    });

    if (!filtered.length) {
      resultsDiv.innerHTML = '<div style="padding:12px;color:#94a3b8;text-align:center">Сотрудники не найдены</div>';
      return;
    }

    resultsDiv.innerHTML = filtered.map(u => {
      const isAlready = currentBranchUsers.has(u.value.toLowerCase());
      return `<div style="display:flex;align-items:center;justify-content:space-between;padding:8px 12px;border-bottom:1px solid #f1f5f9;gap:8px">
        <div>
          <div style="font-weight:600;color:#1e293b;font-size:0.88rem">${escape(u.label || u.login)}</div>
          <small style="color:#64748b">${escape(u.login)}${u.room ? ` · Кабинет ${escape(u.room)}` : ''}</small>
        </div>
        <div>
          ${isAlready ? `
            <span style="font-size:0.8rem;color:#16a34a;font-weight:600">✓ Назначен</span>
          ` : `
            <button type="button" class="primary" data-add-user-login="${escape(u.value)}" style="padding:4px 10px;font-size:0.8rem">
              ＋ Добавить
            </button>
          `}
        </div>
      </div>`;
    }).join('');

    resultsDiv.querySelectorAll('[data-add-user-login]').forEach(btn => {
      btn.addEventListener('click', () => {
        const login = btn.dataset.addUserLogin;
        const bId = branchSelect.value;
        if (!data.codeBlueBranchUsers) data.codeBlueBranchUsers = {};
        if (!data.codeBlueBranchUsers[bId]) data.codeBlueBranchUsers[bId] = [];
        if (!data.codeBlueBranchUsers[bId].some(u => u.toLowerCase() === login.toLowerCase())) {
          data.codeBlueBranchUsers[bId].push(login);
          toast(`Сотрудник ${login} добавлен в филиал`);
          refreshList();
          onUpdate();
        }
      });
    });
  };

  branchSelect.addEventListener('change', refreshList);
  searchInput.addEventListener('input', refreshList);
  refreshList();
}

function openAddSpecialistModal(depts, activeBranches, recipients, getUserLabel, onUpdate) {
  const users = recipients.users || [];
  modal('Добавить сотрудника в филиал и отделение', `
    <form id="modal-spec-form">
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:0.75rem;margin-bottom:0.75rem">
        <label>
          <strong>Филиал:</strong>
          <select id="modal-spec-branch-select" style="width:100%;margin-top:4px" required>
            ${activeBranches.map(b => `<option value="${b.id}">${escape(b.name)}</option>`).join('')}
          </select>
        </label>
        <label>
          <strong>Отделение:</strong>
          <select id="modal-spec-dept-select" style="width:100%;margin-top:4px" required>
            ${depts.map(d => `<option value="${d.id}">${escape(d.name || ('Отделение #' + d.id))}</option>`).join('')}
          </select>
        </label>
      </div>
      <label style="margin-bottom:0.5rem;display:block">
        <strong>Поиск сотрудника:</strong>
        <input type="search" id="modal-spec-search-input" placeholder="Поиск по ФИО, логину, кабинету..." style="width:100%;margin-top:4px">
      </label>
      <div id="modal-spec-user-results" style="max-height:220px;overflow-y:auto;border:1px solid #cbd5e1;border-radius:6px;background:#fff;margin-bottom:1rem">
      </div>
      <div style="display:flex;justify-content:flex-end;gap:0.5rem">
        <button type="button" id="modal-spec-cancel" class="secondary">Закрыть</button>
      </div>
    </form>
  `);

  const branchSelect = $('#modal-spec-branch-select');
  const deptSelect = $('#modal-spec-dept-select');
  const searchInput = $('#modal-spec-search-input');
  const resultsDiv = $('#modal-spec-user-results');
  $('#modal-spec-cancel').addEventListener('click', closeModal);

  const refreshList = () => {
    const q = (searchInput.value || '').trim().toLowerCase();
    const branchId = branchSelect.value;
    const deptId = deptSelect.value;
    const dept = depts.find(d => String(d.id) === String(deptId));
    const currentUsers = new Set((dept?.branchResponsibleUsers?.[branchId] || []).map(u => u.toLowerCase()));

    const filtered = users.filter(u => {
      return (u.label || '').toLowerCase().includes(q) || (u.login || '').toLowerCase().includes(q) || (u.room || '').toLowerCase().includes(q);
    });

    if (!filtered.length) {
      resultsDiv.innerHTML = '<div style="padding:12px;color:#94a3b8;text-align:center">Сотрудники не найдены</div>';
      return;
    }

    resultsDiv.innerHTML = filtered.map(u => {
      const isAlready = currentUsers.has(u.value.toLowerCase());
      return `<div style="display:flex;align-items:center;justify-content:space-between;padding:8px 12px;border-bottom:1px solid #f1f5f9;gap:8px">
        <div>
          <div style="font-weight:600;color:#1e293b;font-size:0.88rem">${escape(u.label || u.login)}</div>
          <small style="color:#64748b">${escape(u.login)}${u.room ? ` · Кабинет ${escape(u.room)}` : ''}</small>
        </div>
        <div>
          ${isAlready ? `
            <span style="font-size:0.8rem;color:#16a34a;font-weight:600">✓ Назначен</span>
          ` : `
            <button type="button" class="primary" data-add-dept-user-login="${escape(u.value)}" style="padding:4px 10px;font-size:0.8rem">
              ＋ Добавить
            </button>
          `}
        </div>
      </div>`;
    }).join('');

    resultsDiv.querySelectorAll('[data-add-dept-user-login]').forEach(btn => {
      btn.addEventListener('click', () => {
        const login = btn.dataset.addDeptUserLogin;
        const bId = branchSelect.value;
        const curDept = depts.find(d => String(d.id) === String(deptSelect.value));
        if (curDept) {
          if (!curDept.branchResponsibleUsers) curDept.branchResponsibleUsers = {};
          if (!curDept.branchResponsibleUsers[bId]) curDept.branchResponsibleUsers[bId] = [];
          if (!curDept.branchResponsibleUsers[bId].some(u => u.toLowerCase() === login.toLowerCase())) {
            curDept.branchResponsibleUsers[bId].push(login);
            toast(`Сотрудник ${login} добавлен в отделение`);
            refreshList();
            onUpdate();
          }
        }
      });
    });
  };

  branchSelect.addEventListener('change', refreshList);
  deptSelect.addEventListener('change', refreshList);
  searchInput.addEventListener('input', refreshList);
  refreshList();
}

function openEditDepartmentsModal(depts, onUpdate) {
  const renderModalContent = () => {
    modal('Редактировать отделения службы вызова', `
      <div style="margin-bottom:1rem">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.75rem;flex-wrap:wrap;gap:0.5rem">
          <p style="margin:0;font-size:0.85rem;color:#64748b">
            Настройте список отделений, цвета и шаблоны должностей специалистов.
          </p>
          <div style="display:flex;gap:0.5rem">
            <button type="button" id="mdept-add-btn" class="primary" style="font-size:0.8rem">＋ Добавить отделение</button>
            <button type="button" id="mdept-sync-btn" class="secondary" style="font-size:0.8rem">🔄 Синхронизировать отделения по филиалам с системы экстренного оповещения</button>
          </div>
        </div>
        <div id="mdept-sync-result" style="font-size:0.85rem;margin-bottom:0.5rem"></div>
        <div id="mdept-list-container" style="display:flex;flex-direction:column;gap:0.5rem;max-height:360px;overflow-y:auto;padding-right:4px">
          ${depts.map((d, idx) => `
            <div style="display:flex;gap:0.5rem;align-items:center;padding:0.6rem;border:1px solid #cbd5e1;border-radius:6px;background:#f8fafc">
              <input type="color" value="${escape(d.color||'#2563eb')}" data-mdept-color="${idx}" style="width:42px;height:36px;border:none;cursor:pointer;background:none" title="Цвет отделения">
              <input value="${escape(d.name)}" placeholder="Название отделения" data-mdept-name="${idx}" style="flex:2" required>
              <input value="${escape(d.specialistTitle||'')}" placeholder="Специалист / шаблон вызова" data-mdept-title="${idx}" style="flex:2">
              <button type="button" class="quiet danger" data-mdept-remove="${idx}" title="Удалить отделение" style="padding:6px 10px;font-size:0.85rem">✕ Удалить</button>
            </div>
          `).join('') || '<div class="empty" style="padding:1rem;background:#fff;border-radius:6px;border:1px dashed #cbd5e1">Отделения пока не созданы.</div>'}
        </div>
      </div>
      <div style="display:flex;justify-content:flex-end;gap:0.5rem">
        <button type="button" id="mdept-close-btn" class="primary">Готово</button>
      </div>
    `);

    $('#mdept-close-btn').addEventListener('click', () => {
      closeModal();
      onUpdate();
    });

    $('#mdept-add-btn').addEventListener('click', () => {
      const nextId = Math.max(0, ...depts.map(d => Number(d.id) || 0)) + 1;
      depts.push({
        id: nextId,
        name: '',
        specialistTitle: '',
        color: '#2563eb',
        responsibleUsers: [],
        responsibleGroups: [],
        branchResponsibleUsers: {}
      });
      renderModalContent();
    });

    $('#mdept-sync-btn').addEventListener('click', async () => {
      const statusEl = $('#mdept-sync-result');
      statusEl.textContent = 'Синхронизация…';
      statusEl.className = '';
      try {
        const res = await api('/settings/emergency/sync-all', 'POST', {});
        statusEl.textContent = 'Успешно синхронизировано. Обновлено рабочих мест: ' + (res.clients ?? 0);
        statusEl.style.color = '#16a34a';
        toast('Отделения и филиалы синхронизированы');
        const fresh = await api('/settings/emergency');
        if (fresh && fresh.departments) {
          depts.length = 0;
          fresh.departments.forEach(d => {
            if (!d.branchResponsibleUsers) d.branchResponsibleUsers = {};
            depts.push(d);
          });
          renderModalContent();
        }
      } catch (err) {
        statusEl.textContent = err.message;
        statusEl.style.color = '#dc2626';
        toast(err.message, true);
      }
    });

    const host = $('#modal-content');
    host.querySelectorAll('[data-mdept-color]').forEach(input => {
      input.addEventListener('input', e => {
        depts[Number(e.target.dataset.mdeptColor)].color = e.target.value;
      });
    });
    host.querySelectorAll('[data-mdept-name]').forEach(input => {
      input.addEventListener('input', e => {
        depts[Number(e.target.dataset.mdeptName)].name = e.target.value;
      });
    });
    host.querySelectorAll('[data-mdept-title]').forEach(input => {
      input.addEventListener('input', e => {
        depts[Number(e.target.dataset.mdeptTitle)].specialistTitle = e.target.value;
      });
    });
    host.querySelectorAll('[data-mdept-remove]').forEach(btn => {
      btn.addEventListener('click', () => {
        const idx = Number(btn.dataset.mdeptRemove);
        if (confirm(`Удалить отделение «${depts[idx].name || 'Без названия'}»?`)) {
          depts.splice(idx, 1);
          renderModalContent();
        }
      });
    });
  };

  renderModalContent();
}

function renderKnowledgeBase(articles) {
  const categories = ['Все', ...new Set(articles.map(a => a.category).filter(Boolean))];
  $('#content').innerHTML = `
    <div class="toolbar">
      <input id="kb-filter" placeholder="Поиск по статьям и ключевым словам...">
      <select id="kb-cat-filter">
        ${categories.map(c => `<option value="${escape(c)}">${escape(c)}</option>`).join('')}
      </select>
      ${can('buttons.manage') ? '<button id="kb-add-btn" class="primary">＋ Создать статью</button>' : ''}
    </div>
    <div class="panel">
      <div class="panel-head"><h2>Статьи базы знаний</h2><span id="kb-count">${articles.length} статей</span></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr><th>ЗАГОЛОВОК</th><th>КАТЕГОРИЯ</th><th>КЛЮЧЕВЫЕ СЛОВА</th><th>ПРОСМОТРЫ</th><th>ОБНОВЛЕНО</th>${can('buttons.manage') ? '<th>ДЕЙСТВИЯ</th>' : ''}</tr>
          </thead>
          <tbody id="kb-rows"></tbody>
        </table>
      </div>
    </div>
  `;

  function drawRows() {
    const q = ($('#kb-filter')?.value || '').toLowerCase();
    const cat = $('#kb-cat-filter')?.value || 'Все';
    const filtered = articles.filter(a => {
      const matchCat = cat === 'Все' || a.category === cat;
      const matchQ = !q || (a.title + ' ' + a.keywords + ' ' + (a.category || '')).toLowerCase().includes(q);
      return matchCat && matchQ;
    });
    $('#kb-count').textContent = filtered.length + ' показано';
    $('#kb-rows').innerHTML = filtered.map(a => `
      <tr>
        <td><strong>${a.isPinned ? '📌 ' : ''}${escape(a.title)}</strong></td>
        <td><span class="status online">${escape(a.category)}</span></td>
        <td><small>${escape(a.keywords || '—')}</small></td>
        <td>${a.viewCount}</td>
        <td><small>${date(a.updatedAt)}</small></td>
        ${can('buttons.manage') ? `<td><div class="actions">
          <button data-kb-view="${a.id}">👁 Просмотр</button>
          <button data-kb-edit="${a.id}">✏ Изменить</button>
          <button data-kb-del="${a.id}">Удалить</button>
        </div></td>` : ''}
      </tr>
    `).join('') || `<tr><td colspan="6"><div class="empty">Статьи не найдены</div></td></tr>`;

    $('#kb-rows').querySelectorAll('[data-kb-view]').forEach(b => b.addEventListener('click', () => {
      const art = articles.find(x => x.id === Number(b.dataset.kbView));
      if (art) viewArticleModal(art);
    }));
    $('#kb-rows').querySelectorAll('[data-kb-edit]').forEach(b => b.addEventListener('click', () => {
      const art = articles.find(x => x.id === Number(b.dataset.kbEdit));
      if (art) editArticleModal(art);
    }));
    $('#kb-rows').querySelectorAll('[data-kb-del]').forEach(b => b.addEventListener('click', async () => {
      if (!confirm('Удалить эту статью?')) return;
      try {
        await api('/knowledge-base/' + b.dataset.kbDel, 'DELETE');
        toast('Статья удалена');
        await loadPage();
      } catch (e) { toast(e.message, true); }
    }));
  }

  $('#kb-filter').addEventListener('input', drawRows);
  $('#kb-cat-filter').addEventListener('change', drawRows);
  if (can('buttons.manage')) $('#kb-add-btn').addEventListener('click', () => editArticleModal(null));
  drawRows();
}

function viewArticleModal(art) {
  modal(art.title, `
    <div style="padding: 10px 0;">
      <div style="margin-bottom: 12px;"><span class="status online">${escape(art.category)}</span> <small style="margin-left: 10px;">Просмотров: ${art.viewCount}</small></div>
      <div style="background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; white-space: pre-wrap; font-family: inherit; line-height: 1.6;">${escape(art.contentMarkdown)}</div>
    </div>
  `);
}

function editArticleModal(art) {
  modal(art ? 'Редактирование статьи' : 'Новая статья базы знаний', `
    <form id="kb-form">
      <label>Заголовок<input name="title" required maxlength="200" value="${escape(art?.title || '')}" placeholder="Например: Как настроить печать"></label>
      <label>Категория<input name="category" required maxlength="100" value="${escape(art?.category || 'General')}" placeholder="DMED, ЕИСЗ, NCALayer, Принтеры и картриджи, ЭЦП"></label>
      <label>Ключевые слова (через запятую)<input name="keywords" maxlength="300" value="${escape(art?.keywords || '')}" placeholder="печать, очередь, бумага"></label>
      <label>Содержимое статьи (Markdown / текст)<textarea name="contentMarkdown" rows="10" required style="width: 100%; border: 1px solid #cbd5e1; border-radius: 6px; padding: 8px;">${escape(art?.contentMarkdown || '')}</textarea></label>
      <label style="display: flex; align-items: center; gap: 8px; margin-top: 8px;"><input type="checkbox" name="isPinned" ${art?.isPinned ? 'checked' : ''}> Закрепить статью вверху списка</label>
      <div class="error" id="form-error"></div>
      <div class="form-actions" style="margin-top: 14px;"><button class="primary" type="submit">Сохранить статью</button></div>
    </form>
  `);
  $('#kb-form').addEventListener('submit', async e => {
    e.preventDefault();
    const fd = new FormData(e.target);
    const body = {
      title: fd.get('title'),
      category: fd.get('category'),
      keywords: fd.get('keywords') || '',
      contentMarkdown: fd.get('contentMarkdown') || '',
      isPinned: fd.get('isPinned') === 'on'
    };
    try {
      if (art) await api('/knowledge-base/' + art.id, 'PUT', body);
      else await api('/knowledge-base', 'POST', body);
      closeModal();
      toast('Статья сохранена');
      await loadPage();
    } catch (err) { $('#form-error').textContent = err.message; }
  });
}



function renderMedicalStatuses(items) {
  const root = $('#settings-tab-body');
  root.innerHTML = '<div class="toolbar"><button id="medical-add" class="primary">＋ Добавить статус</button></div>' +
    table(['СТАТУС','ЗВУК','TTS','ПОРЯДОК','АКТИВНОСТЬ','ДЕЙСТВИЯ'],items.map(s => '<tr><td><strong style="color:'+escape(s.colorHex)+'">'+escape(s.emoji)+' '+escape(s.title)+'</strong><small>'+escape(s.key)+'</small></td><td>'+(s.muteSounds?'Тихий режим':'Включён')+'</td><td>'+(s.muteTts?'Приглушён':'Включён')+'</td><td>'+s.orderIndex+'</td><td>'+(s.isActive?'Активен':'Отключён')+'</td><td><button data-medical-edit="'+s.id+'">Изменить</button>'+(s.key==='Available'?'':'<button data-medical-delete="'+s.id+'">Удалить</button>')+'</td></tr>').join(''));
  $('#medical-add').onclick = () => medicalStatusForm();
  root.querySelectorAll('[data-medical-edit]').forEach(b=>b.onclick=()=>medicalStatusForm(items.find(s=>s.id===Number(b.dataset.medicalEdit))));
  root.querySelectorAll('[data-medical-delete]').forEach(b=>b.onclick=()=>remove('/settings/medical-statuses/'+b.dataset.medicalDelete,'Удалить медицинский статус?'));
}
function medicalStatusForm(s={key:'',title:'',emoji:'🟢',colorHex:'#16A34A',muteSounds:false,muteTts:false,orderIndex:0,isActive:true,description:''}) {
  modal(s.id?'Изменить статус':'Новый статус','<form id="medical-form"><label>Ключ<input name="key" required maxlength="50" value="'+escape(s.key)+'" '+(s.id?'readonly':'')+'></label><label>Название<input name="title" required maxlength="100" value="'+escape(s.title)+'"></label><div class="form-row"><label>Эмодзи<input name="emoji" maxlength="16" value="'+escape(s.emoji)+'"></label><label>Цвет<input type="color" name="colorHex" value="'+escape(s.colorHex)+'"></label></div><label>Порядок<input name="orderIndex" type="number" min="0" max="10000" value="'+s.orderIndex+'"></label><label>Описание<textarea name="description" maxlength="1000">'+escape(s.description)+'</textarea></label>'+['muteSounds','muteTts','isActive'].map((key,i)=>'<label class="check-label"><input type="checkbox" name="'+key+'" '+(s[key]?'checked':'')+'>'+['Тихий режим','Глушить TTS','Активен'][i]+'</label>').join('')+'<p id="medical-error" class="error"></p><button class="primary">Сохранить</button></form>');
  $('#medical-form').onsubmit=async e=>{
    e.preventDefault();const data=Object.fromEntries(new FormData(e.target));data.orderIndex=Number(data.orderIndex);
    ['muteSounds','muteTts','isActive'].forEach(k=>data[k]=e.target.elements[k].checked);
    try {await api('/settings/medical-statuses'+(s.id?'/'+s.id:''),s.id?'PUT':'POST',data);closeModal();await loadPage();}
    catch(error){$('#medical-error').textContent=error.message;}
  };
  decorateSettingsHelp($('#medical-form'),'medical');
}
let emergencyRecipientCatalog;
async function mountUserGroupSelector(root, users, groups, changed) {
  if(!root) return;
  try {
    if(!emergencyRecipientCatalog) emergencyRecipientCatalog=api('/settings/emergency/recipients').then(data=>({users:data.users||[],groups:data.groups||[]}));
    const catalog=await emergencyRecipientCatalog;if(!root.isConnected)return;
    let selectedUsers=[...(users||[])],selectedGroups=[...(groups||[])];
    const chips=document.createElement('div'),input=document.createElement('input'),results=document.createElement('div');
    input.type='search';input.placeholder='Поиск по ФИО, логину, кабинету или внутренней группе';
    results.style.cssText='max-height:160px;overflow:auto;border:1px solid #cbd5e1;border-radius:6px;background:#fff';
    root.replaceChildren(chips,input,results);
    const entries=[...catalog.users.map(u=>({value:u.value,label:(u.label||u.login)+' · '+u.login+(u.room?' · Каб. '+u.room:''),group:false})),...catalog.groups.map(g=>({...g,group:true}))];
    const refresh=()=>{
      chips.replaceChildren(...[...selectedUsers.map(value=>({value,group:false})),...selectedGroups.map(value=>({value,group:true}))].map(item=>{
        const button=document.createElement('button');button.type='button';button.className='quiet';button.textContent=(item.group?'👥 ':'👤 ')+(entries.find(e=>e.value===item.value)?.label||item.value)+' ✕';
        button.onclick=()=>{if(item.group)selectedGroups=selectedGroups.filter(v=>v!==item.value);else selectedUsers=selectedUsers.filter(v=>v!==item.value);changed(selectedUsers,selectedGroups);refresh();};return button;
      }));
      const query=input.value.trim().toLowerCase();
      results.replaceChildren(...entries.filter(e=>e.label.toLowerCase().includes(query)&&!(e.group?selectedGroups:selectedUsers).includes(e.value)).map(item=>{
        const button=document.createElement('button');button.type='button';button.style.cssText='display:block;width:100%;text-align:left;padding:6px 10px;border:none;background:transparent;cursor:pointer';
        button.textContent=(item.group?'👥 ':'👤 ')+item.label;
        button.onmouseover=()=>button.style.background='#f1f5f9';
        button.onmouseout=()=>button.style.background='transparent';
        button.onclick=()=>{(item.group?selectedGroups:selectedUsers).push(item.value);changed(selectedUsers,selectedGroups);refresh();};return button;
      }));
    };
    input.oninput=refresh;refresh();
  }catch(error){root.textContent='Не удалось загрузить сотрудников: '+error.message;}
}
const emergencyCodes=['CODE_RED','CODE_YELLOW','CODE_BLACK','CODE_PINK','CODE_ORANGE','CODE_WHITE','CODE_BLUE'];
const emergencyCodeMeta = {
  CODE_RED: { label: 'Красный код', desc: 'Пожар / Задымление / Эвакуация', color: '#dc2626', icon: '🔴' },
  CODE_YELLOW: { label: 'Жёлтый код', desc: 'ЧС / Внутренняя авария', color: '#ca8a04', icon: '🟡' },
  CODE_BLACK: { label: 'Чёрный код', desc: 'Угроза теракта / Взрывное устройство', color: '#18181b', icon: '⚫' },
  CODE_PINK: { label: 'Розовый код', desc: 'Похищение / потеря ребёнка', color: '#db2777', icon: '🌸' },
  CODE_ORANGE: { label: 'Оранжевый код', desc: 'Опасные вещества / Авария', color: '#ea580c', icon: '🟠' },
  CODE_WHITE: { label: 'Белый код', desc: 'Тихий вызов охраны / Нападение', color: '#475569', icon: '⚪' },
  CODE_BLUE: { label: 'Синий код', desc: 'Реанимация / СЛР', color: '#2563eb', icon: '🔵' }
};

function emergencyCodeMatrix(data, activeBranches=[], getUserLabel=login=>login) {
  const roles=['SuperAdmin','Admin','Operator','Psychologist','User'];
  const langLabels = { ru: 'RU', kk: 'KZ', en: 'EN', zh: 'ZH' };
  const minTableWidth = Math.max(1250, 720 + activeBranches.length * 150);
  return `<div class="emergency-matrix-card" style="margin:0.75rem 0;padding:1rem;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px">
    <div style="margin-bottom:0.75rem">
      <h3 style="margin:0 0 0.25rem 0;font-size:1.05rem;color:#1e293b;display:flex;align-items:center;gap:6px">
        <span>🚨</span> Доступность, роли и языки тревожных кодов
      </h3>
      <p style="margin:0;font-size:0.85rem;color:#64748b">
        Укажите, какие тревожные коды включены в учреждении, каким ролям пользователей разрешён их запуск и на каких языках воспроизводится оповещение.
      </p>
    </div>
    <div style="overflow-x:auto;width:100%;border:1px solid #e2e8f0;border-radius:8px;background:#fff;box-sizing:border-box">
      <table class="table" style="width:100%;min-width:${minTableWidth}px;font-size:0.85rem;border-collapse:collapse;background:#fff">
        <thead>
          <tr style="background:#f1f5f9;color:#475569;text-align:left;border-bottom:2px solid #e2e8f0">
            <th style="padding:10px 12px;width:40px;text-align:center">Вкл</th>
            <th style="padding:10px 12px;min-width:200px;white-space:nowrap">Тревожный код</th>
            <th style="padding:10px 12px;min-width:280px">Роли с правом запуска</th>
            <th style="padding:10px 12px;min-width:130px">Языки вещания</th>
            <th style="padding:10px 12px;min-width:180px;text-align:center">Назначение в филиалы</th>
            ${activeBranches.map(b => `<th style="padding:10px 12px;min-width:150px;border-left:1px solid #e2e8f0;text-align:center;white-space:nowrap">${escape(b.name)}</th>`).join('')}
          </tr>
        </thead>
        <tbody>
          ${emergencyCodes.map(code=>{
            const meta = emergencyCodeMeta[code] || { label: code, desc: '', color: '#2563eb', icon: '⚠️' };
            const isEnabled = !data.allowedCodes || data.allowedCodes.includes(code);
            const langs = ['CODE_WHITE','CODE_BLUE'].includes(code) ? ['ru','kk'] : ['ru','kk','en','zh'];
            const isBlue = code === 'CODE_BLUE';
            return `<tr style="border-bottom:1px solid #f1f5f9">
              <td style="padding:10px 12px;text-align:center;vertical-align:middle">
                <input type="checkbox" data-code-enabled="${code}" ${isEnabled?'checked':''} title="Включить код в организации" style="width:17px;height:17px;cursor:pointer">
              </td>
              <td style="padding:10px 12px;vertical-align:middle;white-space:nowrap">
                <div style="display:flex;align-items:center;gap:8px">
                  <span style="display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:50%;background:${meta.color}18;border:1px solid ${meta.color}44;font-size:12px;flex-shrink:0">${meta.icon}</span>
                  <div>
                    <strong style="color:${meta.color};display:block;line-height:1.2">${escape(meta.label)}</strong>
                    <small style="color:#64748b;font-size:0.75rem">${escape(meta.desc)}</small>
                  </div>
                </div>
              </td>
              <td style="padding:10px 12px;vertical-align:middle">
                <div style="display:flex;flex-wrap:wrap;gap:6px">
                  ${roles.map(role => {
                    const checked = !data.codeLaunchRoles?.[code] || data.codeLaunchRoles[code].includes(role);
                    return `<label class="check-label" style="display:inline-flex;align-items:center;gap:4px;padding:3px 7px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:5px;font-size:0.78rem;cursor:pointer;margin:0;white-space:nowrap">
                      <input type="checkbox" data-code-role="${code}" value="${role}" ${checked?'checked':''}>
                      <span>${escape(roleName(role))}</span>
                    </label>`;
                  }).join('')}
                </div>
              </td>
              <td style="padding:10px 12px;vertical-align:middle">
                <div style="display:flex;flex-wrap:wrap;gap:5px">
                  ${langs.map(lang => {
                    const checked = !data.emergencyCodeLanguages?.[code] || data.emergencyCodeLanguages[code].includes(lang);
                    return `<label class="check-label" style="display:inline-flex;align-items:center;gap:3px;padding:3px 6px;background:#eff6ff;border:1px solid #bfdbfe;border-radius:5px;font-size:0.75rem;cursor:pointer;color:#1e40af;font-weight:600;margin:0;white-space:nowrap">
                      <input type="checkbox" data-code-language="${code}" value="${lang}" ${checked?'checked':''}>
                      <span>${langLabels[lang]||lang.toUpperCase()}</span>
                    </label>`;
                  }).join('')}
                </div>
              </td>
              <td style="padding:10px 12px;vertical-align:middle;text-align:center">
                ${isBlue ? `
                  <button type="button" id="btn-add-code-blue-user" class="secondary" style="font-size:0.8rem;padding:4px 8px;white-space:nowrap">
                    ＋ Добавить сотрудника в филиал
                  </button>
                ` : `<span style="color:#94a3b8;font-size:0.8rem">—</span>`}
              </td>
              ${activeBranches.map(b => {
                if (!isBlue) {
                  return `<td style="padding:10px 12px;vertical-align:middle;border-left:1px solid #f1f5f9;color:#94a3b8;font-size:0.8rem;text-align:center">—</td>`;
                }
                const bUsers = data.codeBlueBranchUsers?.[b.id] || [];
                return `<td style="padding:10px 12px;vertical-align:middle;border-left:1px solid #f1f5f9;text-align:center">
                  <div style="display:flex;flex-wrap:wrap;gap:4px;justify-content:center">
                    ${bUsers.map(login => `
                      <span class="badge" style="background:#eff6ff;color:#1d4ed8;border:1px solid #bfdbfe;border-radius:4px;padding:2px 6px;font-size:0.75rem;display:inline-flex;align-items:center;gap:4px;white-space:nowrap">
                        👤 ${escape(getUserLabel(login))}
                        <button type="button" data-remove-blue-user="${b.id}:${escape(login)}" style="border:none;background:none;color:#dc2626;cursor:pointer;padding:0 2px;font-weight:bold;line-height:1" title="Удалить">✕</button>
                      </span>
                    `).join('') || '<span style="color:#94a3b8;font-size:0.75rem">Не назначены</span>'}
                  </div>
                </td>`;
              }).join('')}
            </tr>`;
          }).join('')}
        </tbody>
      </table>
    </div>
  </div>`;
}
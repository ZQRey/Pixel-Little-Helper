'use strict';
const I18N = {
  ru: {
    flag: '🇷🇺',
    lang_name: 'Русский',
    app_subtitle: 'Ваши коллеги — на связи',
    sso_btn: 'Войти под пользователем Windows',
    ad_account: 'Учётная запись AD',
    ad_login_ph: 'Логин AD',
    password: 'Пароль',
    login_ad_btn: 'Войти через AD',
    dock_me: 'Текущий пользователь',
    dock_all: 'Все чаты',
    dock_personal: 'Личные чаты',
    dock_groups: 'Рабочие группы',
    dock_unread: 'Непрочитанные',
    dock_fav: 'Избранное',
    dock_broadcast: 'Массовая рассылка',
    dock_lang: 'Язык интерфейса',
    dock_theme: 'Переключить тему',
    dock_settings: 'Настройки',
    messages_title: 'Сообщения',
    new_group: 'Создать группу',
    search_ph: 'Поиск сотрудников и групп',
    clear_search: 'Очистить поиск',
    filter_all: 'Все',
    filter_unread: 'Непрочитанные',
    filter_work: 'Работа',
    filter_personal: 'Личные',
    connecting: 'Подключение…',
    connected_as: '● В сети · ',
    reconnecting: 'Переподключение · история доступна',
    no_connection: 'Нет соединения · повтор через 10 секунд',
    back: 'Назад',
    select_chat: 'Выберите диалог',
    chat_sub: 'Сообщения, файлы и рабочие группы',
    search_chat: 'Поиск по переписке',
    chat_info: 'Информация о чате',
    start_conv: 'Начните разговор',
    start_conv_sub: 'Выберите коллегу или рабочую группу слева',
    new_messages: 'Новые сообщения ↓',
    attach_files: 'Прикрепить файлы',
    write_msg_ph: 'Написать сообщение…',
    assistant_emoji: 'Emoji помощника',
    send: 'Отправить',
    drawer_info: 'Информация',
    close: 'Закрыть',
    settings_title: 'Настройки',
    lang_label: 'Язык интерфейса',
    theme_label: 'Тема',
    theme_dark: 'Тёмная',
    theme_light: 'Светлая',
    theme_contrast: 'Высокая контрастность',
    dnd_label: 'Не беспокоить',
    font_size: 'Размер текста',
    browser_notif: 'Разрешить уведомления браузера',
    help_cmd: 'Помощь · команды сообщений',
    sign_out: 'Выйти',
    online: 'В сети',
    offline: 'Не в сети',
    workgroup: 'Рабочая группа',
    typing: ' печатает…',
    today: 'Сегодня',
    empty_history: 'Здесь пока нет сообщений',
    empty_history_sub: 'Напишите первым или прикрепите файл',
    prev_messages: 'Предыдущие сообщения',
    urgent_badge: '⚠ СРОЧНО',
    ack_btn: 'Ознакомился',
    read_mark: ' · ✓✓ Прочитано',
    saved_mark: ' · ✓ Сохранено',
    acked_mark: ' · Ознакомлен',
    sending: 'Отправляется…',
    send_failed: 'Не отправлено · нажмите отправить повторно'
  },
  kk: {
    flag: '🇰🇿',
    lang_name: 'Қазақша',
    app_subtitle: 'Әріптестеріңіз — байланыста',
    sso_btn: 'Windows пайдаланушысы ретінде кіру',
    ad_account: 'AD тіркелгісі',
    ad_login_ph: 'AD логині',
    password: 'Құпия сөз',
    login_ad_btn: 'AD арқылы кіру',
    dock_me: 'Ағымдағы пайдаланушы',
    dock_all: 'Барлық чаттар',
    dock_personal: 'Жеке чаттар',
    dock_groups: 'Жұмыс топтары',
    dock_unread: 'Оқылмағандар',
    dock_fav: 'Таңдаулы',
    dock_broadcast: 'Жаппай тарату',
    dock_lang: 'Интерфейс тілі',
    dock_theme: 'Тақырыпты ауыстыру',
    dock_settings: 'Баптаулар',
    messages_title: 'Хабарламалар',
    new_group: 'Топ құру',
    search_ph: 'Қызметкерлер мен топтарды іздеу',
    clear_search: 'Іздеуді тазалау',
    filter_all: 'Барлығы',
    filter_unread: 'Оқылмаған',
    filter_work: 'Жұмыс',
    filter_personal: 'Жеке',
    connecting: 'Қосылуда…',
    connected_as: '● Желіде · ',
    reconnecting: 'Қайта қосылу · тарих қолжетімді',
    no_connection: 'Байланыс жоқ · 10 секундтан кейін қайталау',
    back: 'Артқа',
    select_chat: 'Диалогты таңдаңыз',
    chat_sub: 'Хабарламалар, файлдар және жұмыс топтары',
    search_chat: 'Чаттан іздеу',
    chat_info: 'Чат туралы ақпарат',
    start_conv: 'Әңгімені бастаңыз',
    start_conv_sub: 'Сол жақтан әріптесті немесе жұмыс тобын таңдаңыз',
    new_messages: 'Жаңа хабарламалар ↓',
    attach_files: 'Файлдарды тіркеу',
    write_msg_ph: 'Хабарлама жазу…',
    assistant_emoji: 'Көмекші эмодзилері',
    send: 'Жіберу',
    drawer_info: 'Ақпарат',
    close: 'Жабу',
    settings_title: 'Баптаулар',
    lang_label: 'Интерфейс тілі',
    theme_label: 'Тақырып',
    theme_dark: 'Күңгірт',
    theme_light: 'Ашық',
    theme_contrast: 'Жоғары контраст',
    dnd_label: 'Мазаламау',
    font_size: 'Мәтін өлшемі',
    browser_notif: 'Браузер хабарландыруларына рұқсат беру',
    help_cmd: 'Көмек · хабарлама командалары',
    sign_out: 'Шығу',
    online: 'Желіде',
    offline: 'Желіде емес',
    workgroup: 'Жұмыс тобы',
    typing: ' жазып жатыр…',
    today: 'Бүгін',
    empty_history: 'Мұнда әлі хабарлама жоқ',
    empty_history_sub: 'Бірінші болып жазыңыз немесе файл тіркеңіз',
    prev_messages: 'Алдыңғы хабарламалар',
    urgent_badge: '⚠ ШҰҒЫЛ',
    ack_btn: 'Таныстым',
    read_mark: ' · ✓✓ Оқылды',
    saved_mark: ' · ✓ Сақталды',
    acked_mark: ' · Танысты',
    sending: 'Жіберілуде…',
    send_failed: 'Жіберілмеді · қайта жіберу үшін басыңыз'
  },
  en: {
    flag: '🇬🇧',
    lang_name: 'English',
    app_subtitle: 'Your colleagues are online',
    sso_btn: 'Sign in with Windows',
    ad_account: 'AD Account',
    ad_login_ph: 'AD Login',
    password: 'Password',
    login_ad_btn: 'Sign in via AD',
    dock_me: 'Current user',
    dock_all: 'All chats',
    dock_personal: 'Direct messages',
    dock_groups: 'Workgroups',
    dock_unread: 'Unread',
    dock_fav: 'Favorites',
    dock_broadcast: 'Broadcast',
    dock_lang: 'Interface language',
    dock_theme: 'Toggle theme',
    dock_settings: 'Settings',
    messages_title: 'Messages',
    new_group: 'Create group',
    search_ph: 'Search colleagues and groups',
    clear_search: 'Clear search',
    filter_all: 'All',
    filter_unread: 'Unread',
    filter_work: 'Work',
    filter_personal: 'Direct',
    connecting: 'Connecting…',
    connected_as: '● Online · ',
    reconnecting: 'Reconnecting · history available',
    no_connection: 'No connection · retrying in 10s',
    back: 'Back',
    select_chat: 'Select a conversation',
    chat_sub: 'Messages, files and workgroups',
    search_chat: 'Search in conversation',
    chat_info: 'Chat information',
    start_conv: 'Start a conversation',
    start_conv_sub: 'Choose a colleague or workgroup from the left',
    new_messages: 'New messages ↓',
    attach_files: 'Attach files',
    write_msg_ph: 'Write a message…',
    assistant_emoji: 'Assistant emojis',
    send: 'Send',
    drawer_info: 'Information',
    close: 'Close',
    settings_title: 'Settings',
    lang_label: 'Language',
    theme_label: 'Theme',
    theme_dark: 'Dark',
    theme_light: 'Light',
    theme_contrast: 'High Contrast',
    dnd_label: 'Do not disturb',
    font_size: 'Font size',
    browser_notif: 'Enable browser notifications',
    help_cmd: 'Help · message commands',
    sign_out: 'Sign out',
    online: 'Online',
    offline: 'Offline',
    workgroup: 'Workgroup',
    typing: ' is typing…',
    today: 'Today',
    empty_history: 'No messages yet',
    empty_history_sub: 'Be the first to write or attach a file',
    prev_messages: 'Load earlier messages',
    urgent_badge: '⚠ URGENT',
    ack_btn: 'Acknowledged',
    read_mark: ' · ✓✓ Read',
    saved_mark: ' · ✓ Delivered',
    acked_mark: ' · Acknowledged',
    sending: 'Sending…',
    send_failed: 'Failed to send · click to retry'
  },
  zh: {
    flag: '🇨🇳',
    lang_name: '中文',
    app_subtitle: '与您的同事保持联系',
    sso_btn: '使用 Windows 账户登录',
    ad_account: 'AD 账户',
    ad_login_ph: 'AD 登录名',
    password: '密码',
    login_ad_btn: '通过 AD 登录',
    dock_me: '当前用户',
    dock_all: '所有会话',
    dock_personal: '私聊',
    dock_groups: '工作群组',
    dock_unread: '未读',
    dock_fav: '收藏',
    dock_broadcast: '群发通知',
    dock_lang: '界面语言',
    dock_theme: '切换主题',
    dock_settings: '设置',
    messages_title: '消息',
    new_group: '新建群组',
    search_ph: '搜索同事和群组',
    clear_search: '清除搜索',
    filter_all: '全部',
    filter_unread: '未读',
    filter_work: '工作',
    filter_personal: '私聊',
    connecting: '连接中…',
    connected_as: '● 在线 · ',
    reconnecting: '正在重连 · 历史记录可用',
    no_connection: '无连接 · 10秒后重试',
    back: '返回',
    select_chat: '选择对话',
    chat_sub: '消息、文件和工作群组',
    search_chat: '搜索聊天记录',
    chat_info: '聊天信息',
    start_conv: '开启对话',
    start_conv_sub: '从左侧选择同事或工作群组',
    new_messages: '新消息 ↓',
    attach_files: '添加附件',
    write_msg_ph: '输入消息…',
    assistant_emoji: '助手表情',
    send: '发送',
    drawer_info: '信息',
    close: '关闭',
    settings_title: '设置',
    lang_label: '界面语言',
    theme_label: '主题风格',
    theme_dark: '深色',
    theme_light: '浅色',
    theme_contrast: '高对比度',
    dnd_label: '请勿打扰',
    font_size: '字号大小',
    browser_notif: '允许浏览器通知',
    help_cmd: '帮助 · 消息快捷指令',
    sign_out: '退出登录',
    online: '在线',
    offline: '离线',
    workgroup: '工作群组',
    typing: ' 正在输入…',
    today: '今天',
    empty_history: '暂无消息',
    empty_history_sub: '发送第一条消息或附件',
    prev_messages: '加载更早消息',
    urgent_badge: '⚠ 紧急',
    ack_btn: '确认已知悉',
    read_mark: ' · ✓✓ 已读',
    saved_mark: ' · ✓ 已送达',
    acked_mark: ' · 已知悉',
    sending: '发送中…',
    send_failed: '发送失败 · 点击重试'
  }
};
function currentLang(){ return localStorage.getItem('helper.chat.lang') || 'ru'; }
function t(k){ const l = currentLang(); return I18N[l]?.[k] || I18N['ru'][k] || k; }
function getLocale(){ return { ru: 'ru-RU', kk: 'kk-KZ', en: 'en-US', zh: 'zh-CN' }[currentLang()] || 'ru-RU'; }
function applyTranslations(){
  document.title = 'PixelHelper · ' + t('messages_title');
  const sub = document.querySelector('#login p'); if(sub) sub.textContent = t('app_subtitle');
  const sso = $('sso'); if(sso) sso.textContent = t('sso_btn');
  const lf = $('login-form');
  if(lf){
    const labels = lf.querySelectorAll('label');
    if(labels[0]){
      const inp = labels[0].querySelector('input');
      labels[0].childNodes[0].textContent = t('ad_account');
      if(inp) inp.placeholder = t('ad_login_ph');
    }
    if(labels[1]){
      labels[1].childNodes[0].textContent = t('password');
    }
    const sBtn = lf.querySelector('button.secondary');
    if(sBtn) sBtn.textContent = t('login_ad_btn');
  }
  const me = $('me'); if(me) me.title = t('dock_me');
  const fAll = document.querySelector('.dock [data-filter="all"]'); if(fAll) fAll.title = t('dock_all');
  const fPer = document.querySelector('.dock [data-filter="personal"]'); if(fPer) fPer.title = t('dock_personal');
  const fGrp = document.querySelector('.dock [data-filter="groups"]'); if(fGrp) fGrp.title = t('dock_groups');
  const fUnr = document.querySelector('.dock [data-filter="unread"]'); if(fUnr) fUnr.title = t('dock_unread');
  const fFav = document.querySelector('.dock [data-filter="favourite"]'); if(fFav) fFav.title = t('dock_fav');
  const bc = $('broadcast'); if(bc) bc.title = t('dock_broadcast');
  const lng = $('lang'); if(lng) lng.title = t('dock_lang');
  const thm = $('theme'); if(thm) thm.title = t('dock_theme');
  const stg = $('settings'); if(stg) stg.title = t('dock_settings');
  const msgH = document.querySelector('.dialogs header strong'); if(msgH) msgH.textContent = t('messages_title');
  const nGrp = $('new-group'); if(nGrp) nGrp.title = t('new_group');
  const srch = $('search'); if(srch) { srch.placeholder = t('search_ph'); srch.setAttribute('aria-label', t('search_ph')); }
  const cSrch = $('clear-search'); if(cSrch) cSrch.title = t('clear_search');
  const fltAll = document.querySelector('.filters button[data-filter="all"]'); if(fltAll) fltAll.textContent = t('filter_all');
  const fltUnr = document.querySelector('.filters button[data-filter="unread"]'); if(fltUnr) fltUnr.textContent = t('filter_unread');
  const fltGrp = document.querySelector('.filters button[data-filter="groups"]'); if(fltGrp) fltGrp.textContent = t('filter_work');
  const fltPer = document.querySelector('.filters button[data-filter="personal"]'); if(fltPer) fltPer.textContent = t('filter_personal');
  if(!state.peer){
    const ct = $('chat-title'); if(ct) ct.textContent = t('select_chat');
    const cs = $('chat-status'); if(cs) cs.textContent = t('chat_sub');
  }
  const bck = $('back'); if(bck) bck.title = t('back');
  const cSrchBtn = $('chat-search'); if(cSrchBtn) cSrchBtn.title = t('search_chat');
  const inf = $('info'); if(inf) inf.title = t('chat_info');
  const empH = document.querySelector('.conversation .empty h2'); if(empH) empH.textContent = t('start_conv');
  const empP = document.querySelector('.conversation .empty p'); if(empP) empP.textContent = t('start_conv_sub');
  const nMsg = $('new-messages'); if(nMsg) nMsg.textContent = t('new_messages');
  const att = $('attach'); if(att) att.title = t('attach_files');
  const inp = $('input'); if(inp) { inp.placeholder = t('write_msg_ph'); inp.setAttribute('aria-label', t('write_msg_ph')); }
  const emj = $('emoji'); if(emj) emj.title = t('assistant_emoji');
  const snd = $('send'); if(snd) snd.title = t('send');
  const drwH = document.querySelector('#drawer header strong'); if(drwH) drwH.textContent = t('drawer_info');
  const cInf = $('close-info'); if(cInf) cInf.title = t('close');
  const cMdl = $('close-modal'); if(cMdl) cMdl.title = t('close');
  const lBtn = $('login-lang');
  if(lBtn){
    const cur = I18N[currentLang()] || I18N['ru'];
    lBtn.textContent = (cur.flag || '🌐') + '  ' + cur.lang_name;
    lBtn.title = t('dock_lang');
  }
}
function setLang(lang){
  if(!I18N[lang]) lang = 'ru';
  localStorage.setItem('helper.chat.lang', lang);
  applyTranslations();
  renderContacts();
  if(state.peer) renderFeed(false);
}

let designTokens;
const $=id=>document.getElementById(id), state={me:null,contacts:[],peer:0,messages:[],filter:'all',files:[],pending:null,ws:null,generation:0,busy:false,refreshing:false,urls:[],extras:{},groupEvents:[],typingSent:0};
const emojis={helper_wave:['👋','Привет'],helper_joy:['😊','Радость'],helper_party:['🎉','Ура'],helper_thanks:['👍','Спасибо'],helper_sad:['😔','Грусть'],helper_surprise:['😮','Удивление'],helper_laugh:['😂','Смех'],helper_sleep:['😴','Сон'],helper_think:['🤔','Думаю'],helper_dizzy:['😵','Головокружение']};
function element(tag,className,text){const n=document.createElement(tag);if(className)n.className=className;if(text!==undefined)n.textContent=text;return n;}
function button(text,action,className){const n=element('button',className,text);n.type='button';n.onclick=()=>Promise.resolve(action()).catch(showError);return n;}
function showError(e){$('error').textContent=e.message||String(e);}
function toast(text){$('toast').textContent=text;$('toast').hidden=false;setTimeout(()=>$('toast').hidden=true,4000);}
async function api(path,body,method){const headers={'X-Messenger-Web':'1'},options={credentials:'same-origin',signal:AbortSignal.timeout(body instanceof FormData?300000:20000),headers,method:method||(body===undefined?'GET':'POST')};if(body!==undefined){if(body instanceof FormData)options.body=body;else{headers['Content-Type']='application/json';options.body=JSON.stringify(body);}}const response=await fetch('/api/messenger/'+path,options).catch(e=>{throw Error(e.name==='TimeoutError'||e.name==='AbortError'?'Сервер не ответил вовремя. Для автовхода проверьте настройки браузера или войдите через форму AD.':'Нет связи с сервером. Повторите попытку.');});if(!response.ok){if(response.status===401&&state.me){logoutLocal();}let text=response.status===401?'Не удалось подтвердить учётную запись AD.':'Запрос не выполнен ('+response.status+')';try{text=(await response.json()).error||text;}catch{}const error=Error(text);error.status=response.status;throw error;}if(response.headers.get('content-type')?.includes('application/json'))return response.json();return null;}
function avatar(name,online){const n=element('div','avatar',name.trim().split(/\s+/).slice(0,2).map(x=>x[0]).join('').toUpperCase());if(online!==undefined)n.append(element('i','presence'+(online?' online':'')));return n;}
function modal(title){$('modal-title').textContent=title;$('modal-body').replaceChildren();$('modal').showModal();return $('modal-body');}
function logoutLocal(){state.generation++;state.ws?.close();state.ws=null;state.me=null;state.messages=[];state.files=[];state.peer=0;$('app').hidden=true;$('login').hidden=false;}
async function signIn(path,body){if(state.loginBusy)return;state.loginBusy=true;document.querySelectorAll('#login button').forEach(b=>b.disabled=true);$('login-error').textContent='Подключение…';try{state.me=await api(path,body);await start();}catch(e){$('login-error').textContent=e.message;}finally{state.loginBusy=false;document.querySelectorAll('#login button').forEach(b=>b.disabled=false);}}
async function start(){state.me=await api('me');$('login').hidden=true;$('app').hidden=false;$('me').textContent=state.me.fullName.split(/\s+/).slice(0,2).map(x=>x[0]).join('');$('me').title=state.me.fullName;$('error').textContent='';await refresh();connect();}
$('login-form').onsubmit=e=>{e.preventDefault();const form=new FormData(e.target);signIn('web/login',{username:form.get('username'),password:form.get('password')}).finally(()=>e.target.elements.password.value='');};$('sso').onclick=()=>signIn('web/windows');
function setTheme(theme){document.documentElement.dataset.theme=theme;localStorage.setItem('helper.chat.theme',theme);if(typeof designTokens!=='undefined')applyTokens();}$('theme').onclick=()=>setTheme(document.documentElement.dataset.theme==='dark'?'light':'dark');setTheme(localStorage.getItem('helper.chat.theme')||'dark');
document.querySelectorAll('[data-filter]').forEach(n=>n.onclick=()=>{state.filter=n.dataset.filter;document.querySelectorAll('[data-filter]').forEach(b=>b.classList.toggle('selected',b.dataset.filter===state.filter));renderContacts();});$('search').oninput=renderContacts;$('clear-search').onclick=()=>{$('search').value='';renderContacts();};
function renderContacts(){const term=$('search').value.trim().toLocaleLowerCase();$('contacts').replaceChildren();state.contacts.filter(c=>(c.fullName+' '+c.username+' '+(c.branch||'')).toLocaleLowerCase().includes(term)&&(state.filter==='all'||state.filter==='unread'&&c.unread>0||state.filter==='groups'&&c.isGroup||state.filter==='personal'&&!c.isGroup||state.filter==='favourite'&&c.favourite)).sort((a,b)=>Number(b.pinned)-Number(a.pinned)||Number(b.unread>0)-Number(a.unread>0)||new Date(b.lastAt||0)-new Date(a.lastAt||0)||a.fullName.localeCompare(b.fullName)).forEach(c=>{const row=button('',()=>openChat(c.id),'contact'+(c.id===state.peer?' active':''));row.setAttribute('role','listitem');row.append(avatar(c.fullName,c.isGroup?undefined:c.isOnline));const content=element('div','contact-content'),top=element('div','contact-top'),bottom=element('div','contact-bottom');top.append(element('strong','',(c.pinned?'📌 ':'')+c.fullName+(c.muted?' · ◌':'')),element('time','',c.lastAt?new Date(c.lastAt).toLocaleTimeString(getLocale(),{hour:'2-digit',minute:'2-digit'}):''));bottom.append(element('span','',c.lastText?plainEmoji(c.lastText):c.isGroup?t('workgroup'):(c.isOnline?t('online'):t('offline'))+(c.branch?' · '+c.branch:'')));if(c.unread)bottom.append(element('b','badge',c.unread));content.append(top,bottom);row.append(content);row.oncontextmenu=e=>{e.preventDefault();contactActions(c);};$('contacts').append(row);});}
async function refresh(){if(!state.me||state.refreshing)return;state.refreshing=true;try{const [contacts,prefs]=await Promise.all([api('users'),api('preferences')]);state.contacts=contacts.map(c=>Object.assign(c,(()=>{const p=prefs.find(p=>p.peer===c.id);return {pinned:!!p?.pinned,favourite:!!p?.favourite,muted:!!p?.muted};})()));renderContacts();if(state.peer<0&&!state.contacts.some(c=>c.id===state.peer)){state.peer=0;state.messages=[];state.groupEvents=[];state.extras=[];state.pending=null;$('input').value='';$('input').disabled=$('send').disabled=true;$('chat-title').textContent='Группа недоступна';$('close-info').click();renderFeed(false);}if(state.peer)await loadHistory();}catch(e){showError(e);}finally{state.refreshing=false;}}
async function openChat(peer){if(state.busy)return;state.peer=peer;state.messages=[];state.files=[];state.pending=null;$('input').value='';renderFiles();$('app').classList.add('chat-open');$('error').textContent='';renderContacts();await loadHistory();if(!$('drawer').hidden)await info();}
async function loadHistory(before){const peer=state.peer;if(!peer)return;const c=state.contacts.find(c=>c.id===peer);$('chat-title').textContent=c?.fullName||'Диалог';$('chat-status').textContent=c?.isGroup?t('workgroup'):c?.isOnline?t('online'):t('offline');$('chat-avatar').replaceWith(Object.assign(avatar(c?.fullName||'PH',c?.isGroup?undefined:c?.isOnline),{id:'chat-avatar'}));$('input').disabled=$('send').disabled=!c?.isActive||state.busy;if(c?.suspendedUntil&&new Date(c.suspendedUntil)>new Date()){state.messages=[];renderFeed(false);throw Error('Доступ отключён до '+new Date(c.suspendedUntil).toLocaleString(getLocale()));}const rows=await api('history/'+peer+(before?'?before='+before:''));const events=peer<0?(await api('groups/'+(-peer))).events||[]:[];if(peer!==state.peer)return;state.groupEvents=events;const atBottom=$('feed').scrollHeight-$('feed').scrollTop-$('feed').clientHeight<90, oldHeight=$('feed').scrollHeight, oldTop=$('feed').scrollTop;const previousIds=new Set(state.messages.map(m=>m.id));state.messages=before?[...rows,...state.messages]:[...state.messages,...rows];state.messages=[...new Map(state.messages.map(m=>[m.id,m])).values()].sort((a,b)=>a.id-b.id);state.extras=await api("extras/"+peer+"?ids="+state.messages.slice(-100).map(m=>m.id).join(","));if(peer!==state.peer)return;renderFeed(atBottom&&!before);if(before)$('feed').scrollTop=oldTop+$('feed').scrollHeight-oldHeight;else if(!atBottom&&rows.some(m=>!previousIds.has(m.id)))$('new-messages').hidden=false;await markRead();}
function robotEmoji(key){const frames={helper_wave:'greeting',helper_joy:'joy',helper_thanks:'success',helper_sad:'sad',helper_surprise:'surprise',helper_laugh:'laugh',helper_sleep:'sleep',helper_think:'think',helper_party:'celebrate',helper_dizzy:'dizzy'},span=element('span','helper-emoji');span.title=emojis[key][1];for(let i=1;i<=2;i++){const img=element('img');img.src='/messenger/emoji/'+frames[key]+'_'+i+'.png';img.alt=i===1?emojis[key][1]:'';span.append(img);}return span;}
function plainEmoji(text){return text.replace(/:(helper_\w+):/g,(match,key)=>emojis[key]?.[0]||match);}
function richText(text){const n=element('div','message-text');for(const part of text.split(/(https?:\/\/[^\s<>]+|:helper_\w+:)/g)){if(/^https?:\/\//.test(part)){const a=element('a','',part);a.href=part;a.target='_blank';a.rel='noopener noreferrer';n.append(a);}else if(emojis[part.slice(1,-1)]){n.append(robotEmoji(part.slice(1,-1)));}else n.append(document.createTextNode(part));}return n;}
function renderFeed(bottom){state.urls.forEach(URL.revokeObjectURL);state.urls=[];const feed=$('feed');feed.replaceChildren();if(!state.messages.length&&!state.pending){const empty=element('div','empty');empty.append(element('h2','',t('empty_history')),element('p','',t('empty_history_sub')));feed.append(empty);}if(state.messages.length>=50)feed.append(button(t('prev_messages'),()=>loadHistory(state.messages[0].id),'date'));let lastDate='';for(const m of [...state.messages.filter(m=>m.body.trim()||m.attachments?.length),...(state.peer<0?state.groupEvents.filter(e=>!state.messages.length||new Date(e.sentAt)>=new Date(state.messages[0].sentAt)).map(e=>({...e,isSystem:true})):[])].sort((a,b)=>new Date(a.sentAt)-new Date(b.sentAt))){const date=new Date(m.sentAt),day=date.toLocaleDateString(getLocale());if(day!==lastDate){lastDate=day;feed.append(element('div','date',day===new Date().toLocaleDateString(getLocale())?t('today'):day));}if(m.isSystem){feed.append(element('div','date',date.toLocaleTimeString(getLocale(),{hour:'2-digit',minute:'2-digit'})+' · '+m.body));continue;}const mine=m.senderId===state.me.id,bubble=element('article','bubble'+(mine?' mine':''));if(state.peer<0&&!mine)bubble.append(element('div','sender',m.senderName||''));if(m.isUrgent)bubble.append(element('strong','',t('urgent_badge')));bubble.append(richText(m.body));bubble.oncontextmenu=e=>{e.preventDefault();const body=modal('Реакция на сообщение');for(const emoji of ['👍','❤️','😊','🎉','😔','👋'])body.append(button(emoji,async()=>{await api('reaction/'+state.peer+'/'+m.id,{emoji});$('modal').close();await loadHistory();},'secondary'));};for(const file of m.attachments||[]){bubble.append(button('⇩ '+file.name+' · '+(file.size/1024).toFixed(1)+' КБ',()=>download(file),'file'));if(/\.(png|jpe?g|gif|bmp)$/i.test(file.name)){const image=element('img','image-thumb');image.alt=file.name;image.loading='lazy';image.src='/api/messenger/files/'+file.id;image.onclick=()=>viewImage(file);bubble.append(image);}}if(m.isUrgent&&!mine&&!m.acknowledgedAt)bubble.append(button(t('ack_btn'),async()=>{await api('ack/'+m.id,{});await refresh();},'secondary'));bubble.append(element('small','',date.toLocaleTimeString(getLocale(),{hour:'2-digit',minute:'2-digit'})+(mine?(m.readAt?' · ✓✓ Прочитано':' · ✓ Сохранено'):m.acknowledgedAt?' · Ознакомлен':'')));const chips=element('div','reactions');for(const reaction of state.extras[m.id]||[])chips.append(button(reaction.emoji+' '+reaction.count,async()=>{await api('reaction/'+state.peer+'/'+m.id,{emoji:reaction.emoji});await loadHistory();},reaction.mine?'selected':''));bubble.append(chips);feed.append(bubble);}if(state.pending&&state.pending.peer===state.peer){const pending=element('article','bubble mine');pending.append(richText(state.pending.body.replace(new RegExp('^(?:(?:/срочно|/танец)(?:\\s+|$))+','iu'),'')),element('small','',state.pending.failed?'Не отправлено · нажмите отправить повторно':'Отправляется…'));feed.append(pending);}if(bottom){feed.scrollTop=feed.scrollHeight;$('new-messages').hidden=true;}}
async function markRead(){if(!state.me||!state.peer||document.hidden||!document.hasFocus()||$('feed').scrollHeight-$('feed').scrollTop-$('feed').clientHeight>100)return;const latest=state.messages.at(-1);const contact=state.contacts.find(c=>c.id===state.peer);if(latest&&contact?.unread){await api('read/'+state.peer+'/'+latest.id,{});contact.unread=0;renderContacts();}}
$('new-messages').onclick=()=>{$('feed').scrollTop=$('feed').scrollHeight;$('new-messages').hidden=true;markRead().catch(showError);};$('feed').onscroll=()=>{if($('feed').scrollHeight-$('feed').scrollTop-$('feed').clientHeight<90)markRead().catch(showError);};window.addEventListener('focus',()=>markRead().catch(showError));$('back').onclick=()=>$('app').classList.remove('chat-open');
function addFiles(files){const all=[...state.files,...files];if(all.length>10||all.some(f=>f.size>50*1024*1024)||all.reduce((n,f)=>n+f.size,0)>100*1024*1024)throw Error('До 10 файлов: 50 МБ каждый, 100 МБ всего.');state.files=all;renderFiles();}
function renderFiles(){$('pending-files').replaceChildren();state.files.forEach((f,i)=>$('pending-files').append(button(f.name+' ×',()=>{state.files.splice(i,1);renderFiles();})));}
$('attach').onclick=()=>$('file-input').click();$('file-input').onchange=e=>{try{addFiles(Array.from(e.target.files));}catch(e){showError(e);}e.target.value='';};$('input').onpaste=e=>{const files=Array.from(e.clipboardData?.files||[]);if(files.length){e.preventDefault();try{addFiles(files);}catch(e){showError(e);}}};$('input').oninput=()=>{if(state.peer&&state.ws?.readyState===WebSocket.OPEN&&Date.now()-state.typingSent>2000){state.typingSent=Date.now();state.ws.send(JSON.stringify({type:1,target:'Typing',arguments:[state.peer]})+'\u001e');}$('input').style.height='auto';$('input').style.height=Math.min(140,$('input').scrollHeight)+'px';};$('input').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();send().catch(showError);}};$('send').onclick=()=>send().catch(showError);
async function send(){if(state.busy||!state.peer)return;const body=$('input').value.trim();if(!body&&!state.files.length)return;const signature=JSON.stringify([state.peer,body,state.files.map(f=>[f.name,f.size,f.lastModified])]);if(state.pending?.signature!==signature)state.pending={id:crypto.randomUUID(),peer:state.peer,body,signature};state.pending.failed=false;state.busy=true;const peer=state.peer;$('send').disabled=true;renderFeed(true);try{let message;if(state.files.length){const form=new FormData();form.append('peer',peer);form.append('body',body);form.append('clientId',state.pending.id);state.files.forEach(f=>form.append('files',f,f.name));message=await api('files/send',form);}else message=await api('send',{recipientId:peer,body,clientId:state.pending.id});state.messages.push(message);state.pending=null;state.files=[];$('input').value='';$('input').style.height='auto';renderFiles();$('error').textContent='';renderFeed(true);await refresh();}catch(e){if(state.pending)state.pending.failed=true;renderFeed(true);throw e;}finally{state.busy=false;$('send').disabled=!state.contacts.find(c=>c.id===state.peer)?.isActive;}}
$('emoji').onclick=()=>{$('emoji-picker').hidden=!$('emoji-picker').hidden;};for(const [key,[icon,label]]of Object.entries(emojis))$('emoji-picker').append(button(icon,()=>{const input=$('input');input.setRangeText(':'+key+':',input.selectionStart,input.selectionEnd,'end');input.focus();$('emoji-picker').hidden=true;},'emoji-choice')); $("emoji-picker").querySelectorAll('button').forEach((b,i)=>{b.replaceChildren(robotEmoji(Object.keys(emojis)[i]));});
async function download(file){const response=await fetch('/api/messenger/files/'+file.id,{credentials:'same-origin'});if(!response.ok)throw Error('Файл недоступен');const url=URL.createObjectURL(await response.blob()),link=element('a');link.href=url;link.download=file.name;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
function viewImage(file){const body=modal(file.name),image=element('img');image.src='/api/messenger/files/'+file.id;image.alt=file.name;body.append(image,button('Скачать изображение',()=>download(file),'primary'));}
$('info').onclick=()=>info().catch(showError);$('close-info').onclick=()=>{$('drawer').hidden=true;$('app').classList.remove('has-info');};$('close-modal').onclick=()=>$('modal').close();
async function groupOperation(group,operation,body={}){await api('groups/'+group.id+'/'+operation,body);$('modal').close();if(operation==='delete'||operation==='leave'){state.peer=0;state.messages=[];state.groupEvents=[];state.pending=null;$('input').value='';$('input').disabled=$('send').disabled=true;$('close-info').click();renderFeed(false);}await refresh();if(state.peer===-group.id&&!$('drawer').hidden)await info();}
function groupActions(body,group){const owner=group.ownerId===state.me.id,manage=!group.isClosed&&(owner||group.members.some(m=>m.id===state.me.id&&m.isAdmin));if(manage){body.append(button('Пригласить сотрудника',()=>invite(group),'secondary'),button('Изменить название',async()=>{const name=prompt('Название группы',group.name);if(name)await groupOperation(group,'rename',{name});},'secondary'));}body.append(button('Выйти из группы',async()=>{if(confirm('Выйти из группы? Создателю нужно сначала передать владение.'))await groupOperation(group,'leave');},'secondary'));if(owner)body.append(button('Удалить группу',async()=>{if(confirm('Удалить группу для всех участников? История и файлы сохранятся на сервере.'))await groupOperation(group,'delete');},'secondary'));}
async function info(){if(!state.peer)return;$('drawer').hidden=false;$('app').classList.add('has-info');const c=state.contacts.find(c=>c.id===state.peer);if(!c)return;const profile=$('profile');profile.replaceChildren(avatar(c.fullName,c.isGroup?undefined:c.isOnline),element('h2','',c.fullName),element('p','muted',c.isGroup?'Рабочая группа':c.username+(c.branch?' · '+c.branch:'')));if(c.isGroup){const peer=state.peer,group=await api('groups/'+(-peer));if(peer!==state.peer)return;const owner=group.ownerId===state.me.id,manage=!group.isClosed&&(owner||group.members.some(m=>m.id===state.me.id&&m.isAdmin));profile.append(element('h3','',group.members.length+' участников'));for(const member of group.members){const row=element('div','member'),text=element('div','',member.fullName);text.append(element('small','',(member.id===group.ownerId?'Создатель':member.isAdmin?'Администратор':member.username)+(member.suspendedUntil?' · Отключён до '+new Date(member.suspendedUntil).toLocaleString(getLocale()):'')));row.append(avatar(member.fullName,member.isOnline),text);if(manage&&member.id!==state.me.id&&member.id!==group.ownerId&&(owner||!member.isAdmin))row.oncontextmenu=e=>{e.preventDefault();manageMember(group,member);};profile.append(row);}profile.append(button('Настройки группы…',()=>{const body=modal(group.name);groupActions(body,group);},'secondary'));if(group.events?.length){profile.append(element('h3','','События группы'));for(const event of group.events)profile.append(element('p','muted',new Date(event.sentAt).toLocaleString(getLocale())+' · '+event.body));}}profile.append(button('Файлы и изображения',()=>{const body=modal('Вложения в загруженной истории');for(const message of state.messages)for(const file of message.attachments||[])body.append(button(file.name,()=>download(file),'file'));}));}
function manageMember(group,member){const body=modal(member.fullName),actions=[['Исключить из группы','remove'],['Временно отключить','suspend'],['Восстановить доступ','resume']];if(group.ownerId===state.me.id)actions.push([member.isAdmin?'Снять права администратора':'Назначить администратором',member.isAdmin?'unadmin':'admin'],['Передать владение','owner']);for(const [title,op]of actions)body.append(button(title,async()=>{let minutes=0;if(op==='suspend'){minutes=Number(prompt('На сколько минут? От 1 до 43200','60'));if(!Number.isInteger(minutes)||minutes<1||minutes>43200)return;}if(!confirm(title+' — '+member.fullName+(minutes?' на '+minutes+' минут':'')+'?'))return;await groupOperation(group,op,{userId:member.id,minutes});},'secondary'));}
function employeePicker(body,available,multiple,chosen=new Set()){const search=element('input');search.placeholder='Имя, логин или филиал';const list=element('div','employee-list');function render(){list.replaceChildren();for(const c of available.filter(c=>(c.fullName+' '+c.username+' '+(c.branch||'')).toLocaleLowerCase().includes(search.value.toLocaleLowerCase()))){const row=element('label'),check=element('input');check.type=multiple?'checkbox':'radio';check.name='employee';check.checked=chosen.has(c.id);check.onchange=()=>{if(!multiple)chosen.clear();check.checked?chosen.add(c.id):chosen.delete(c.id);};row.append(check,document.createTextNode(c.fullName+' · '+c.username));list.append(row);}}search.oninput=render;body.append(search,list);render();return chosen;}
async function invite(group){const body=modal('Пригласить в '+group.name),chosen=employeePicker(body,state.contacts.filter(c=>!c.isGroup&&c.isActive&&!group.members.some(m=>m.id===c.id)),false);body.append(button('Пригласить',async()=>{if(!chosen.size)return;await api('groups/'+group.id+'/invite',{userId:[...chosen][0]});$('modal').close();await refresh();if(state.peer===-group.id)await info();},'primary'));}
async function inviteToGroup(contact){const body=modal('Добавить '+contact.fullName+' в группу');for(const group of state.contacts.filter(c=>c.isGroup&&(c.ownerId===state.me.id||c.isAdmin)&&c.isActive))body.append(button(group.fullName,async()=>{if(confirm('Пригласить '+contact.fullName+' в '+group.fullName+'?')){await api('groups/'+(-group.id)+'/invite',{userId:contact.id});$('modal').close();await refresh();}},'secondary'));}
$('new-group').onclick=()=>{const body=modal('Новая группа'),name=element('input');name.placeholder='Название группы';name.maxLength=80;body.append(name);const chosen=employeePicker(body,state.contacts.filter(c=>!c.isGroup&&c.isActive),true);body.append(button('Создать группу',async()=>{const group=await api('groups',{name:name.value,members:[...chosen]});$('modal').close();await refresh();await openChat(group.id);},'primary'));};
$('broadcast').onclick=()=>broadcast().catch(showError);
$('lang').onclick=()=>{const body=modal(t('lang_label'));for(const [code,item] of Object.entries(I18N)){body.append(button(item.flag+'  '+item.lang_name,()=>{setLang(code);$('modal').close();},code===currentLang()?'primary':'secondary'));}};
$('settings').onclick=async()=>{
  const body=modal(t('settings_title'));
  const langSel=element('select');
  for(const [code,item] of Object.entries(I18N)){
    const opt=element('option','',item.flag+'  '+item.lang_name);
    opt.value=code;
    langSel.append(opt);
  }
  langSel.value=currentLang();
  langSel.onchange=()=>{setLang(langSel.value);$('settings').click();};
  const font=element('input');
  font.type='range';font.min=12;font.max=22;font.value=localStorage.getItem('helper.chat.font')||14;
  font.oninput=()=>{document.documentElement.style.fontSize=font.value+'px';localStorage.setItem('helper.chat.font',font.value);};
  const themes=element('select');
  for(const [value,label] of [['dark',t('theme_dark')],['light',t('theme_light')],['contrast',t('theme_contrast')]]){
    const o=element('option','',label);
    o.value=value;
    themes.append(o);
  }
  themes.value=document.documentElement.dataset.theme;
  themes.onchange=()=>setTheme(themes.value);
  const quiet=element('label'),quietBox=element('input');
  quietBox.type='checkbox';quietBox.checked=localStorage.getItem('helper.chat.quiet')==='true';
  quietBox.onchange=()=>localStorage.setItem('helper.chat.quiet',String(quietBox.checked));
  quiet.append(quietBox,document.createTextNode(t('dnd_label')));
  body.append(
    element('p','',t('lang_label')),langSel,
    element('p','',t('theme_label')),themes,
    quiet,
    element('p','',t('font_size')),font,
    button(t('browser_notif'),async()=>{if(!('Notification'in window))return toast('Браузер не поддерживает уведомления');toast('Разрешение: '+await Notification.requestPermission());}),
    button(t('help_cmd'),()=>{
      const help=modal(t('help_cmd'));
      help.append(
        element('p','','/срочно Текст — срочное уведомление поверх окон.'),
        element('p','','/танец — помощник получателя танцует. /танец Текст — танец и сообщение.'),
        element('p','','Команды пишутся в начале и не отображаются в переписке. Можно сочетать: /срочно /танец Текст. Требуется клиент 1.2.25.')
      );
    })
  );
  const caps=await api('capabilities');
  body.append(button(t('sign_out'),async()=>{await api('web/logout',{});$('modal').close();logoutLocal();}));
};
document.documentElement.style.fontSize=(localStorage.getItem('helper.chat.font')||14)+'px';
async function broadcast(){const body=modal('Массовая рассылка'),text=element('textarea'),audience=element('select'),chosen=new Set();for(const [value,label]of[['all','Все включённые пользователи'],['online','Только онлайн'],['selected','Выбранные сотрудники']]){const option=element('option','',label);option.value=value;audience.append(option);}const picker=element('div','broadcast-recipients');picker.hidden=true;employeePicker(picker,state.contacts.filter(c=>!c.isGroup&&c.isActive),true,chosen);audience.onchange=()=>picker.hidden=audience.value!=='selected';const label=element('label'),urgent=element('input');urgent.type='checkbox';label.append(urgent,document.createTextNode('Срочное · помощник поверх окон до 60 секунд'));const status=element('p');let pendingId=null,signature=null,sending=false;label.className='broadcast-urgency';body.append(audience,picker,element('h4','','Тип сообщения'),label,text,status,button('Просмотреть и отправить',async()=>{if(sending||!text.value.trim())return;const recipients=state.contacts.filter(c=>!c.isGroup&&c.isActive&&(audience.value==='all'||audience.value==='online'&&c.isOnline||audience.value==='selected'&&chosen.has(c.id)));if(!recipients.length)throw Error('Выберите получателей.');if(!confirm((urgent.checked?'СРОЧНО':'Обычное сообщение')+' · Получателей сейчас: '+recipients.length+'\n\n'+text.value+'\n\nОтправить?'))return;const next=JSON.stringify([text.value,audience.value,urgent.checked,[...chosen].sort()]);if(next!==signature){signature=next;pendingId=crypto.randomUUID();}const form=new FormData();form.append('body',text.value);form.append('audience',audience.value);form.append('urgent',String(urgent.checked));form.append('clientId',pendingId);if(audience.value==='selected')for(const id of chosen)form.append('recipients',id);sending=true;try{const result=await api('broadcast',form);toast('Отправлено: '+result.recipients);$('modal').close();await refresh();}catch(e){status.textContent=e.message;}finally{sending=false;}},'primary'),button('Мои рассылки · отчёт',async()=>{const rows=await api('broadcasts');status.textContent='';for(const row of rows)status.append(element('p','',row.body+' · Получателей: '+row.recipients+' · Прочитали: '+row.read+' · Подтвердили: '+row.acknowledged));}));}
$('chat-search').onclick=()=>{const body=modal('Поиск по всей переписке'),input=element('input'),results=element('div');input.placeholder='Фраза от двух символов';let timer;input.oninput=()=>{clearTimeout(timer);const phrase=input.value;timer=setTimeout(async()=>{try{results.replaceChildren();if(phrase.trim().length<2)return;const rows=await api('search/'+state.peer+'?q='+encodeURIComponent(phrase));if(input.value!==phrase)return;for(const m of rows)results.append(element('p','',new Date(m.sentAt).toLocaleString(getLocale())+' · '+m.body));}catch(e){results.textContent=e.message;}},200);};body.append(input,results);};
function contactActions(c){const body=modal(c.fullName);for(const [label,key]of[['Закрепить / открепить','pinned'],['Избранное / убрать','favourite'],['Без звука / включить','muted']])body.append(button(label,async()=>{const next={pinned:!!c.pinned,favourite:!!c.favourite,muted:!!c.muted};next[key]=!next[key];await api('preferences/'+c.id,next);$('modal').close();await refresh();},'secondary'));if(!c.isGroup)body.append(button('Добавить в группу',()=>inviteToGroup(c),'secondary'));else{body.append(button('Настройки группы…',async()=>{const group=await api('groups/'+(-c.id));const body=modal(c.fullName);groupActions(body,group);},'secondary'));}}
function notify(message){if(!message.body.trim()&&!message.attachments?.length)return;if(message.senderId===state.me?.id)return;if(document.hasFocus()&&!document.hidden&&state.peer===(message.recipientId<0?message.recipientId:message.senderId))return;const quiet=localStorage.getItem('helper.chat.quiet')==='true';if(quiet||state.contacts.find(c=>c.id===(message.recipientId<0?message.recipientId:message.senderId))?.muted)return;if('Notification'in window&&Notification.permission==='granted'){const notification=new Notification(message.isUrgent?'СРОЧНО · PixelHelper':'PixelHelper',{body:plainEmoji(message.body).slice(0,200),tag:'helper-'+message.id});notification.onclick=()=>{window.focus();openChat(message.recipientId<0?message.recipientId:message.senderId).catch(showError);};}}
async function connect(){const generation=++state.generation;state.ws?.close();try{const response=await fetch('/messengerHub/negotiate?negotiateVersion=1',{method:'POST',credentials:'same-origin',headers:{'X-Messenger-Web':'1'}});if(!response.ok)throw Error('Не удалось подключить уведомления');const negotiation=await response.json();if(generation!==state.generation||!state.me)return;const ws=new WebSocket(location.origin.replace(/^http/,'ws')+'/messengerHub?id='+encodeURIComponent(negotiation.connectionToken));state.ws=ws;let handshake=false,buffer='',last=Date.now();const timer=setInterval(()=>{if(Date.now()-last>45000)ws.close();else if(ws.readyState===WebSocket.OPEN)ws.send('{"type":6}\u001e');},15000);const timeout=setTimeout(()=>{if(!handshake)ws.close();},10000);ws.onopen=()=>ws.send('{"protocol":"json","version":1}\u001e');ws.onmessage=e=>{last=Date.now();buffer+=e.data;let split;while((split=buffer.indexOf('\u001e'))>=0){const raw=buffer.slice(0,split);buffer=buffer.slice(split+1);if(!raw)continue;const packet=JSON.parse(raw);if(!handshake){if(packet.error){ws.close();return;}handshake=true;$('connection').textContent='● В сети · '+state.me.fullName;clearTimeout(timeout);refresh();}else if(packet.type===1){if(packet.target==='Typing'){const t=packet.arguments[0];if(t.peer===state.peer){$('chat-status').textContent=t.fullName+' печатает…';setTimeout(()=>{if(state.peer===t.peer)$('chat-status').textContent=state.contacts.find(c=>c.id===t.peer)?.isOnline?'В сети':'Рабочий чат';},5000);}continue;}if(packet.target==='ChatMessage')notify(packet.arguments[0]);refresh();}else if(packet.type===7)ws.close();}};ws.onclose=()=>{clearInterval(timer);clearTimeout(timeout);if(generation===state.generation&&state.me){$('connection').textContent='Переподключение · история доступна';setTimeout(()=>{if(generation===state.generation&&state.me)connect();},5000);}};ws.onerror=()=>ws.close();}catch(e){$('connection').textContent='Нет соединения · повтор через 10 секунд';if(state.me&&generation===state.generation)setTimeout(()=>{if(generation===state.generation&&state.me)connect();},10000);}}
setInterval(()=>{if(state.me&&!document.hidden)refresh();},30000);$('me').onclick=()=>toast(state.me?.fullName||'');if($('login-lang')) $('login-lang').onclick=()=>$('lang').onclick();applyTranslations();(async()=>{if(location.protocol!=='https:'){$('login-error').textContent='Для входа откройте эту страницу через HTTPS.';return;}try{await start();}catch(e){logoutLocal();if(e.status===401)await signIn('web/windows');else $('login-error').textContent=e.message;}})();

fetch("/messenger/design-tokens.json").then(r=>r.json()).then(tokens=>{designTokens=tokens;applyTokens();}).catch(()=>{});function applyTokens(){if(!designTokens)return;for(const [key,value]of Object.entries(designTokens[document.documentElement.dataset.theme]||designTokens.dark))document.documentElement.style.setProperty("--"+key,value);}
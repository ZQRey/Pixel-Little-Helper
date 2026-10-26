"use strict";
function aiSecureOrigin(){return location.protocol==='https:'||['localhost','127.0.0.1','[::1]'].includes(location.hostname);}
function aiHttpsNotice(host){const text=document.createElement('p');text.textContent='Для ИИ-чата и настройки ключей откройте защищённый адрес: ';const link=document.createElement('a');const url=new URL(location.href);url.protocol='https:';link.href=url.href;link.textContent=url.origin;host.replaceChildren(text,link);}
function aiHelp(label,text){return typeof fieldHelp==='function'?fieldHelp(label,text):'';}
async function renderAiSettings() {
  const host = document.querySelector('#settings-tab-body');
  if (!host) return;
  if(!aiSecureOrigin()){aiHttpsNotice(host);return;}
  const data = await api('/ai/settings');
  if (!host.isConnected) return;
  const value = data.options;
  host.innerHTML = `<div class="card"><h2>ИИ-помощник ${aiHelp('ИИ-помощник','Настройки определяют, какие пользователи и клиенты могут обращаться к модели, какие провайдеры и модели разрешены, а также лимиты запросов.')}</h2>
    <p>Облачный чат и OpenCode используют модели провайдера. Каждый новый разговор имеет отдельную историю.</p>
    <form id="ai-settings-form">
      <h3>Доступ</h3>
      <label class="ai-check ai-wide"><input name="enabled" type="checkbox" ${value.enabled?'checked':''}> Включить ИИ ${aiHelp('Включить ИИ','Пока переключатель выключен, новые разговоры и диагностика запрещены. Изменение ключей также выключает ИИ до явной повторной проверки.')}</label>
      <label class="ai-check ai-wide"><input name="pilotOnly" type="checkbox" ${value.pilotOnly!==false?'checked':''}> Ограниченный пилот ${aiHelp('Ограниченный пилот','Пилот разрешает ИИ только выбранным обычным пользователям. Администраторы и супер-администраторы с нужным правом обходят это ограничение.')}</label>
      <div class="ai-wide" id="ai-pilot-section">
        <div class="ai-help-label">Участники пилота ${aiHelp('Участники пилота','Выберите обычных пользователей, которым разрешён ИИ. Список не заменяет права доступа: пользователю всё равно нужно право ai.chat или ai.agent.')}</div>
        <input id="ai-pilot-search" type="search" maxlength="200" placeholder="Поиск по имени, логину или роли">
        <div id="ai-pilot-list" class="ai-picker-list" role="group" aria-label="Участники пилота"></div>
        <p id="ai-pilot-status" role="status"></p>
      </div>
      <label class="ai-check ai-wide"><input name="useKnowledge" type="checkbox" ${value.useKnowledge?'checked':''}> Передавать модели разрешённые пользователю фрагменты базы знаний ${aiHelp('База знаний','В модель попадают только статьи, доступные пользователю по роли и филиалу. Перед повторной отправкой старых сообщений доступ проверяется снова.')}</label>
      <label class="ai-check ai-wide"><input name="diagnosticsEnabled" type="checkbox" ${value.diagnosticsEnabled?'checked':''}> Разрешить диагностику администраторам с правом ai.diagnostics ${aiHelp('Диагностика','Разрешает только чтение состояния ПК и проверки HTTP из фиксированного списка. Право ai.diagnostics и роль администратора обязательны.')}</label>

      <h3>Подключение</h3>
      <label>Подключение ${aiHelp('Подключение','CloudApi обращается к облачному OpenAI-совместимому API. OpenCode использует выделенный локальный worker; в персональном режиме у каждого пользователя свой worker и ключ.')}<select name="backend"><option value="CloudApi">Облачный API</option><option value="OpenCode">OpenCode</option></select></label>
      <div id="ai-cloud-section" class="ai-wide">
        <label>Адрес облачного API ${aiHelp('Адрес облачного API','Корневой HTTPS-адрес OpenAI-совместимого API, например https://openrouter.ai/api/v1/. Адрес должен заканчиваться косой чертой или будет нормализован.')}<input name="baseUrl" type="url" required value="${escape(value.baseUrl)}"></label>
        <label>API-ключ ${data.apiKeyConfigured?'(сохранён)':'(не задан)'} ${aiHelp('API-ключ','Секрет хранится зашифрованным и никогда не возвращается в браузер. Пустое поле сохраняет прежний ключ.')}<input name="apiKey" type="password" autocomplete="new-password" placeholder="Оставьте пустым, чтобы сохранить ключ"></label>
        <div class="ai-secret-row"><span id="ai-cloud-key-status">${data.apiKeyConfigured?'Ключ сохранён':'Ключ не задан'}</span><button type="button" id="ai-delete-cloud" class="danger" ${data.apiKeyConfigured?'':'disabled'}>Удалить облачный ключ</button></div>
      </div>
      <div id="ai-opencode-mode-wrap">
        <label>Режим OpenCode ${aiHelp('Режим OpenCode','Общий worker использует корпоративный адрес и пароль. Персональный режим направляет разговоры только на worker владельца; без назначенного включённого worker запросы отклоняются.')}<select name="openCodeMode"><option value="Shared">Общий корпоративный worker</option><option value="Personal">Отдельный worker для каждого пользователя</option></select></label>
      </div>
      <div id="ai-opencode-shared" class="ai-wide">
        <label>Адрес OpenCode ${aiHelp('Адрес OpenCode','Внутренний HTTPS-адрес или адрес Docker-сервиса общего worker. Пользовательский адрес задать нельзя.')}<input name="openCodeUrl" type="url" required value="${escape(value.openCodeUrl)}"></label>
        <label>Пароль OpenCode ${data.openCodePasswordConfigured?'(сохранён)':'(не задан)'} ${aiHelp('Пароль OpenCode','Basic-пароль общего worker. Хранится зашифрованным, в браузер не возвращается. Пустое поле сохраняет прежний пароль.')}<input name="openCodePassword" type="password" autocomplete="new-password" placeholder="Оставьте пустым, чтобы сохранить пароль"></label>
        <div class="ai-secret-row"><span id="ai-opencode-password-status">${data.openCodePasswordConfigured?'Пароль сохранён':'Пароль не задан'}</span><button type="button" id="ai-delete-opencode" class="danger" ${data.openCodePasswordConfigured?'':'disabled'}>Удалить пароль OpenCode</button></div>
        <button type="button" id="ai-configure-code">Передать сохранённый ключ провайдера в OpenCode</button>
      </div>
      <div id="ai-opencode-personal" class="ai-wide">
        <p>Для персонального режима назначьте отдельный worker ниже. Пользователь вводит личный ключ провайдера в своём ИИ-чате; корпоративный ключ в персональные worker не передаётся.</p>
      </div>

      <h3 id="ai-models-title">Разрешённые модели</h3><p id="ai-models-note">Это allowlist, а не весь список моделей провайдера. Чтобы увидеть остальные модели, загрузите каталог сохранённого подключения ниже и добавьте нужные. Платные и модели с неизвестной ценой можно сохранить; их запуск пока недоступен.</p>
      <div id="ai-models-editor"><div id="ai-models"></div><button type="button" id="ai-add-model">Добавить модель вручную</button></div>
      <div class="ai-wide" id="ai-catalog-section">
        <button type="button" id="ai-load-catalog">Загрузить каталог сохранённого подключения</button>
        <label id="ai-catalog-owner-wrap">Владелец каталога ${aiHelp('Владелец каталога','Для персонального OpenCode выберите пользователя с назначенным worker. Каталог загружается с его worker и содержит только модели его provider.')}<select name="catalogOwner"><option value="">Сначала назначьте worker</option></select></label>
        <p id="ai-catalog-note">Каталог не включает модели автоматически. Цены сообщает провайдер; перед включением проверьте тариф и условия обработки данных.</p>
        <label>Поиск в каталоге<input id="ai-catalog-search" type="search" maxlength="200"></label>
        <label class="ai-check"><input id="ai-catalog-free" type="checkbox" checked> Только бесплатные</label>
        <p id="ai-catalog-status" role="status"></p><div id="ai-catalog"></div>
      </div>
      <label id="ai-shared-model-wrap">Модель OpenCode для всех ${aiHelp('Модель для всех','Выберите бесплатную модель из каталога самого OpenCode. Она будет использоваться всеми пользователями общего worker; вводить идентификатор вручную не нужно.')}<select name="sharedModel"><option value="">Загрузка каталога OpenCode…</option></select></label>
      <label id="ai-default-model-wrap">Модель по умолчанию ${aiHelp('Модель по умолчанию','Используется для нового разговора. Если выбранная модель удалена, панель автоматически выбирает первую бесплатную. Платная модель не позволяет включить ИИ.')}<select name="defaultModel"></select></label>
      <h3>Лимиты</h3>
      <label>Запросов на пользователя за 24 часа ${aiHelp('Лимит запросов','Общий лимит генераций и диагностических проверок на пользователя за скользящие 24 часа.')}<input name="requestsPerDay" type="number" min="1" max="10000" value="${value.requestsPerDay}"></label>
      <label>Максимум токенов ответа ${aiHelp('Максимум токенов','Верхняя граница ответа модели. Слишком большое значение увеличивает расход и время ответа.')}<input name="maxOutputTokens" type="number" min="32" max="4096" value="${value.maxOutputTokens}"></label>
      <label>Тайм-аут, секунд ${aiHelp('Тайм-аут','Максимальное время одного ответа провайдера. При превышении генерация отменяется без автоматического повтора.')}<input name="timeoutSeconds" type="number" min="10" max="180" value="${value.timeoutSeconds}"></label>
      <label>Хранить историю, дней ${aiHelp('Срок хранения','Старые разговоры и служебные записи очищаются фоновой задачей. Метаданные запросов без текста хранятся до 90 дней.')}<input name="retentionDays" type="number" min="1" max="365" value="${value.retentionDays}"></label>
      <h3>Сайты для диагностики</h3><p>Разрешайте точные URL без параметров и конкретные IP сайта. Проверки не читают содержимое страниц и не переходят по перенаправлениям.</p>
      <div id="ai-targets" class="ai-wide"></div><button id="ai-add-target" type="button" class="ai-wide">Добавить сайт</button>
      <p>Изменение настроек требует нового разговора. Личные ключи вводятся пользователем в его ИИ-чате.</p>
      <div class="toolbar"><button class="primary" type="submit">Сохранить</button><button type="button" id="ai-test">Проверить сохранённое подключение</button></div>
      <p id="ai-settings-result" role="status"></p>
    </form><details id="ai-workers"><summary>Персональные workers OpenCode</summary>
      <p>Сначала подготовьте отдельный контейнер и volume для каждого пользователя. Привязка адреса и владельца постоянна; повторное назначение запрещено. Личный ключ пользователь вводит в своём ИИ-чате.</p>
      <form id="ai-worker-form"><label>Пользователь ${aiHelp('Владелец worker','Выберите активного пользователя. Один пользователь может иметь только один worker; адрес нельзя переназначить другому владельцу.')}<select name="ownerUserId" required><option value="">Выберите пользователя</option></select></label><label>Корневой URL worker ${aiHelp('URL worker','Корневой внутренний адрес выделенного OpenCode worker без пути и параметров.')}<input name="url" type="url" required></label>
      <label>Пароль worker ${aiHelp('Пароль worker','Basic-пароль выделенного worker, не личный ключ провайдера. Минимум 16 символов; после сохранения не отображается.')}<input name="password" type="password" autocomplete="new-password" minlength="16" required></label><label>Provider ID ${aiHelp('Provider ID','Идентификатор провайдера в OpenCode. Для него в разрешённых моделях должна быть хотя бы одна бесплатная модель.')}<input name="provider" maxlength="100" required></label>
      <button type="submit">Назначить worker</button></form><button type="button" id="ai-workers-refresh">Обновить список</button><p id="ai-worker-status" role="status"></p><div id="ai-worker-list"></div></details></div>${aiChatMarkup(true)}`;
  const form = host.querySelector('#ai-settings-form');
  form.elements.backend.value = value.backend;
  form.elements.openCodeMode.value = value.openCodeMode || 'Shared';
  const rows = host.querySelector('#ai-models');
  let users = [];
  let catalog = [];
  let selectedPilots = new Set(value.pilotUserIds || []);
  let keyConfigured = !!data.apiKeyConfigured;
  let passwordConfigured = !!data.openCodePasswordConfigured;
  const personalMode = () => form.elements.backend.value === 'OpenCode' && form.elements.openCodeMode.value === 'Personal';
  const sharedMode = () => form.elements.backend.value === 'OpenCode' && form.elements.openCodeMode.value === 'Shared';
  const modelRows = () => [...rows.children].map(row=>({id:row.querySelector('[data-model-id]').value.trim(),provider:row.querySelector('[data-provider]').value.trim(),price:row.querySelector('[data-price]').value}));
  const selectedSharedModel = () => catalog.find(model => model.id === form.elements.sharedModel.value && model.price === 'Free');
  const models = () => sharedMode() ? (selectedSharedModel() ? [(({id,provider,price}) => ({id,provider,price}))(selectedSharedModel())] : []) : modelRows();
  function selectedOwner() { return users.find(user => String(user.id) === form.elements.catalogOwner.value); }
  function freeModels() {
    const owner = personalMode() ? selectedOwner() : null;
    return models().filter(model => model.price === 'Free' && (!owner || !owner.workerProvider || model.provider === owner.workerProvider));
  }
  function refreshDefault() {
    const select = form.elements.defaultModel;
    const shared = form.elements.sharedModel;
    if (sharedMode()) {
      const free = catalog.filter(m=>m.price === 'Free');
      const current = shared.value || value.defaultModel;
      shared.innerHTML = free.length
        ? free.map(m=>`<option value="${escape(m.id)}">${escape(m.name||m.id)} · ${escape(m.provider)} / ${escape(m.id)}</option>`).join('')
        : '<option value="">Нет бесплатных моделей в каталоге OpenCode</option>';
      if (free.some(m=>m.id===current)) shared.value = current;
      else if (free.length) shared.value = free[0].id;
      else shared.value = '';
      const chosen = free.find(m=>m.id===shared.value);
      select.innerHTML = chosen ? `<option value="${escape(chosen.id)}">${escape(chosen.id)}</option>` : '';
      select.value = chosen?.id || '';
      select.disabled = true;
      shared.disabled = free.length === 0;
      form.querySelector('button[type="submit"]').disabled = free.length === 0 && form.elements.enabled.checked;
      if (!free.length && form.elements.enabled.checked) host.querySelector('#ai-settings-result').textContent = 'В каталоге OpenCode нет бесплатной модели. Загрузите каталог и выключите ИИ до выбора модели.';
      return;
    }
    const current = select.value || value.defaultModel;
    const all = models();
    select.innerHTML = all.map(m=>`<option value="${escape(m.id)}">${escape(m.id)} · ${escape(m.price)}${m.provider?' · '+escape(m.provider):''}</option>`).join('');
    const free = freeModels();
    if (all.some(m=>m.id===current) && (all.find(m=>m.id===current)?.price === 'Free' || !form.elements.enabled.checked)) select.value = current;
    else if (free.length) select.value = free[0].id;
    else if (all.length) select.value = '';
    const noFree = all.length > 0 && free.length === 0;
    select.disabled = all.length === 0;
    form.querySelector('button[type="submit"]').disabled = noFree && form.elements.enabled.checked;
    if (noFree && form.elements.enabled.checked) host.querySelector('#ai-settings-result').textContent = 'Нет бесплатной модели, совместимой с подключением. Добавьте бесплатную модель или отключите ИИ.';
  }
  function lockModelRows() {
    const locked = form.elements.backend.value === 'OpenCode';
    rows.querySelectorAll('input,select').forEach(input => { if (input.dataset.modelId || input.dataset.provider || input.dataset.price) input.readOnly = locked; });
    rows.querySelectorAll('select').forEach(select => select.disabled = locked);
    host.querySelector('#ai-add-model').hidden = locked;
  }
  function addModel(model={id:'',provider:'',price:'Unknown'}) {
    const row = document.createElement('div'); row.className='toolbar ai-model-row';
    row.innerHTML=`<label>ID модели ${aiHelp('ID модели','Точный идентификатор модели у провайдера. Для OpenCode модель добавляется только из каталога.')}<input data-model-id maxlength="200" required value="${escape(model.id)}"></label><label>Provider ID ${aiHelp('Provider ID модели','Идентификатор провайдера OpenCode. Для облачного API может быть пустым.')}<input data-provider maxlength="100" value="${escape(model.provider)}"></label><label>Цена ${aiHelp('Цена модели','Free разрешает запуск. Paid и Unknown можно сохранить, но выполнение заблокировано до появления бюджетов.')}<select data-price><option>Free</option><option>Paid</option><option>Unknown</option></select></label><button type="button">Удалить</button>`;
    row.querySelector('[data-price]').value=model.price;
    row.querySelector('button').onclick=()=>{row.remove();refreshDefault();};
    row.addEventListener('change',refreshDefault); rows.append(row); refreshDefault();
  }
  value.models.forEach(addModel);
  function applyVisibility() {
    const backend = form.elements.backend.value;
    const mode = form.elements.openCodeMode.value;
    form.querySelector('#ai-cloud-section').hidden = backend !== 'CloudApi';
    form.querySelector('#ai-opencode-mode-wrap').hidden = backend !== 'OpenCode';
    form.querySelector('#ai-opencode-shared').hidden = !(backend === 'OpenCode' && mode === 'Shared');
    form.querySelector('#ai-opencode-personal').hidden = !(backend === 'OpenCode' && mode === 'Personal');
    form.querySelector('#ai-catalog-owner-wrap').hidden = !(backend === 'OpenCode' && mode === 'Personal');
    form.querySelector('#ai-test').hidden = backend === 'OpenCode' && mode === 'Personal';
    form.querySelector('#ai-models-editor').hidden = backend === 'OpenCode';
    form.querySelector('#ai-default-model-wrap').hidden = backend === 'OpenCode' && mode === 'Shared';
    form.querySelector('#ai-shared-model-wrap').hidden = !(backend === 'OpenCode' && mode === 'Shared');
    host.querySelector('#ai-workers').hidden = !(backend === 'OpenCode' && mode === 'Personal');
    host.querySelector('#ai-models-title').textContent = sharedMode() ? 'Модель OpenCode из каталога' : 'Разрешённые модели';
    host.querySelector('#ai-models-note').textContent = sharedMode()
      ? 'Список берётся напрямую из OpenCode. Выберите одну бесплатную модель — её будут использовать все пользователи; ввод ID вручную не требуется.'
      : backend === 'OpenCode'
      ? 'Для персонального OpenCode модели добавляются только из каталога. Модели другого provider не будут доступны персональному worker.'
      : 'Бесплатность проверяйте по каталогу провайдера. Платные и модели с неизвестной ценой можно сохранить; их запуск пока недоступен.';
    host.querySelector('#ai-catalog-note').textContent = sharedMode()
      ? 'Каталог запрашивается у самого OpenCode и показывает только подключённые провайдеры. Встроенный провайдер opencode доступен без ключа для бесплатных моделей.'
      : 'Каталог не включает модели автоматически. Цены сообщает провайдер; перед включением проверьте тариф и условия обработки данных.';
    lockModelRows();
    refreshDefault();
  }
  const pilotSearch = host.querySelector('#ai-pilot-search');
  function pilotLabel(user) {
    const role = roleName(user.role);
    return `${escape(user.fullName || user.username)} · ${escape(user.username)} · ${escape(role)}${user.workerEnabled?' · worker':' '}`;
  }
  function renderPilotList() {
    const query = pilotSearch.value.trim().toLowerCase();
    const list = host.querySelector('#ai-pilot-list');
    const filtered = users.filter(user => `${user.fullName} ${user.username} ${roleName(user.role)}`.toLowerCase().includes(query));
    list.innerHTML = filtered.map(user => `<label class="ai-picker-row"><input type="checkbox" data-pilot-id="${user.id}" ${selectedPilots.has(user.id)?'checked':''}><span>${pilotLabel(user)}</span></label>`).join('') || '<p>Пользователи не найдены.</p>';
    list.querySelectorAll('[data-pilot-id]').forEach(input => input.onchange = () => { const id=Number(input.dataset.pilotId); input.checked?selectedPilots.add(id):selectedPilots.delete(id); updatePilotStatus(); });
    updatePilotStatus();
  }
  function updatePilotStatus() {
    const missing = [...selectedPilots].filter(id => !users.some(user => user.id === id));
    host.querySelector('#ai-pilot-status').textContent = `Выбрано: ${selectedPilots.size}.${missing.length?` Не найдены активные пользователи: ${missing.join(', ')}.`:''}`;
  }
  async function loadUsers() {
    try {
      users = await api('/ai/settings/users');
      if (!host.isConnected) return;
      renderPilotList();
      const owner = form.elements.catalogOwner;
      const workers = users.filter(user => user.hasWorker);
      owner.innerHTML = '<option value="">Выберите владельца worker</option>' + workers.map(user => `<option value="${user.id}" ${user.workerEnabled?'':'disabled'}>${escape(user.fullName || user.username)} · ${escape(user.workerProvider)}${user.workerEnabled?'':user.workerReady?' · отключён':' · ключ не настроен'}</option>`).join('');
      const first = workers.find(user => user.workerEnabled);
      if (first) owner.value = String(first.id);
      const creator = host.querySelector('#ai-worker-form').elements.ownerUserId;
      creator.innerHTML = '<option value="">Выберите пользователя</option>' + users.filter(user => !user.hasWorker).map(user => `<option value="${user.id}">${escape(user.fullName || user.username)} · ${escape(user.username)}</option>`).join('');
      refreshDefault();
    } catch (error) {
      host.querySelector('#ai-pilot-status').textContent = error.message;
    }
  }
  pilotSearch.oninput = renderPilotList;
  form.elements.backend.onchange = () => { applyVisibility(); if(sharedMode()&&!catalog.length)loadCatalog(true); };
  form.elements.openCodeMode.onchange = () => { applyVisibility(); if(sharedMode()&&!catalog.length)loadCatalog(true); };
  form.elements.sharedModel.onchange = () => { refreshDefault(); showCatalog(); };
  form.elements.enabled.onchange = refreshDefault;
  form.elements.catalogOwner.onchange = () => { refreshDefault(); showCatalog(); };
  const targetRows = host.querySelector('#ai-targets');
  const targets=()=>[...targetRows.children].map(row=>({id:row.querySelector('[data-target-id]').value.trim(),title:row.querySelector('[data-target-title]').value.trim(),url:row.querySelector('[data-target-url]').value.trim(),allowedAddresses:row.querySelector('[data-target-addresses]').value.split(/[\s,;]+/).filter(Boolean)}));
  function addTarget(target={id:'',title:'',url:'',allowedAddresses:[]}){
    const row=document.createElement('div');row.className='ai-target-row';
    row.innerHTML=`<label>ID ${aiHelp('ID сайта','Короткий идентификатор для выбора в диагностике: латинские буквы, цифры, дефис или подчёркивание.')}<input data-target-id required maxlength="60" value="${escape(target.id)}"></label><label>Название ${aiHelp('Название сайта','Понятное название, которое увидит администратор при выборе проверки.')}<input data-target-title required maxlength="100" value="${escape(target.title)}"></label><label>URL ${aiHelp('URL сайта','Точный адрес без параметров. Проверяется только HTTP-ответ; содержимое страницы не читается.')}<input data-target-url type="url" required value="${escape(target.url)}"></label><label>Разрешённые IP ${aiHelp('Разрешённые IP','Список конкретных адресов сайта через запятую. После DNS-проверки соединение разрешено только с ними; loopback и служебные адреса запрещены.')}<input data-target-addresses required value="${escape(target.allowedAddresses.join(', '))}"></label><button type="button">Удалить</button>`;
    row.querySelector('button').onclick=()=>row.remove();targetRows.append(row);
  }
  (value.diagnosticTargets||[]).forEach(addTarget);host.querySelector('#ai-add-target').onclick=()=>addTarget();
  host.querySelector('#ai-add-model').onclick=()=>addModel();
  function showCatalog(){
    const list=host.querySelector('#ai-catalog');list.replaceChildren();
    const query=host.querySelector('#ai-catalog-search').value.trim().toLowerCase();
    const owner=personalMode()?selectedOwner():null;
    const shared=sharedMode();
    const filtered=catalog.filter(m=>(shared||!host.querySelector('#ai-catalog-free').checked||m.price==='Free')&&(!shared||m.price==='Free')&&(!owner||!owner.workerProvider||m.provider===owner.workerProvider)&&`${m.id} ${m.provider} ${m.name}`.toLowerCase().includes(query));
    for(const model of filtered.slice(0,100)){
      const row=document.createElement('div');row.className='toolbar';
      const label=document.createElement('span');label.textContent=`${model.name} · ${model.provider ? model.provider+' / ' : ''}${model.id} · ${model.price}`;
      const button=document.createElement('button');button.type='button';button.textContent=shared?'Использовать для всех':'Добавить в разрешённые';
      button.disabled=shared?false:modelRows().some(m=>m.id===model.id);
      if(shared&&form.elements.sharedModel.value===model.id)button.textContent='Выбрано для всех';
      button.onclick=()=>{if(shared){form.elements.sharedModel.value=model.id;refreshDefault();showCatalog();}else{addModel(model);showCatalog();}};row.append(label,button);list.append(row);
    }
    host.querySelector('#ai-catalog-status').textContent=`Найдено: ${filtered.length}. Показано: ${Math.min(filtered.length,100)}. ${shared?'Выберите модель и нажмите «Сохранить».':'Изменения нужно сохранить.'}`;
  }
  host.querySelector('#ai-catalog-search').oninput=showCatalog;
  host.querySelector('#ai-catalog-free').onchange=showCatalog;
  async function loadCatalog(silent=false){
    const button=host.querySelector('#ai-load-catalog');button.disabled=true;
    const output=host.querySelector('#ai-catalog-status');if(!silent)output.textContent='Загрузка каталога…';
    try{const personal=personalMode();if(personal&&!form.elements.catalogOwner.value)throw Error('Выберите владельца персонального worker.');const result=await api('/ai/settings/catalog'+(personal?'?owner='+encodeURIComponent(form.elements.catalogOwner.value):''));if(!host.isConnected)return;if(result.backend!==form.elements.backend.value||(result.openCodeMode&&result.openCodeMode!==form.elements.openCodeMode.value))throw Error('Сначала сохраните выбранное подключение, затем загрузите его каталог.');catalog=result.models;if(sharedMode())refreshDefault();showCatalog();}
    catch(error){catalog=[];host.querySelector('#ai-catalog').replaceChildren();output.textContent=error.message;if(sharedMode())refreshDefault();}
    finally{button.disabled=false;}
  }
  host.querySelector('#ai-load-catalog').onclick=()=>loadCatalog();
  form.onsubmit=async event=>{
    event.preventDefault(); const output=host.querySelector('#ai-settings-result');
    const update={...value,models:models(),diagnosticTargets:targets()};
    update.pilotUserIds=[...selectedPilots];
    for(const name of ['backend','openCodeMode','baseUrl','apiKey','openCodeUrl','openCodePassword','defaultModel'])update[name]=form.elements[name].value;
    for(const name of ['enabled','pilotOnly','useKnowledge','diagnosticsEnabled'])update[name]=form.elements[name].checked;
    for(const name of ['requestsPerDay','maxOutputTokens','timeoutSeconds','retentionDays'])update[name]=Number(form.elements[name].value);
    if (sharedMode()) {
      if (!form.elements.sharedModel.value) { output.textContent='Загрузите каталог OpenCode и выберите бесплатную модель для всех.'; return; }
      update.defaultModel = form.elements.sharedModel.value;
      update.models = models();
    }
    if (update.enabled && models().find(model => model.id === update.defaultModel)?.price !== 'Free') { output.textContent='Выберите бесплатную модель по умолчанию или выключите ИИ.'; return; }
    if (update.enabled && update.pilotOnly && update.pilotUserIds.length === 0) { output.textContent='Добавьте хотя бы одного участника пилота или отключите ограниченный пилот.'; return; }
    try { await api('/ai/settings','PUT',update);form.elements.apiKey.value='';form.elements.openCodePassword.value='';output.textContent='Сохранено. Для проверки откройте новый разговор.'; }
    catch(error){output.textContent=error.message;}
  };
  host.querySelector('#ai-test').onclick=async()=>{
    const output=host.querySelector('#ai-settings-result');output.textContent='Проверка…';
    try{await api('/ai/settings/test','POST',{});output.textContent='Подключение доступно. Проверка ответа модели выполняется в чате ниже.';}catch(error){output.textContent=error.message;}
  };
  host.querySelector('#ai-delete-cloud').onclick=async event=>{
    if(!confirm('Удалить сохранённый облачный ключ? ИИ будет выключен, пока ключ не зададут снова.'))return;
    const button=event.currentTarget;button.disabled=true;
    try{await api('/ai/settings/cloud-key','DELETE');keyConfigured=false;form.elements.enabled.checked=false;form.elements.apiKey.value='';host.querySelector('#ai-cloud-key-status').textContent='Ключ не задан';host.querySelector('#ai-settings-result').textContent='Облачный ключ удалён. ИИ выключен.';}catch(error){button.disabled=false;host.querySelector('#ai-settings-result').textContent=error.message;}
  };
  host.querySelector('#ai-delete-opencode').onclick=async event=>{
    if(!confirm('Удалить сохранённый пароль OpenCode? ИИ будет выключен, пока пароль не зададут снова.'))return;
    const button=event.currentTarget;button.disabled=true;
    try{await api('/ai/settings/opencode-password','DELETE');passwordConfigured=false;form.elements.enabled.checked=false;form.elements.openCodePassword.value='';host.querySelector('#ai-opencode-password-status').textContent='Пароль не задан';host.querySelector('#ai-settings-result').textContent='Пароль OpenCode удалён. ИИ выключен.';}catch(error){button.disabled=false;host.querySelector('#ai-settings-result').textContent=error.message;}
  };
  host.querySelector('#ai-configure-code').onclick=async()=>{
    const output=host.querySelector('#ai-settings-result');
    if(!confirm('Обновить общий ключ провайдера в выделенном OpenCode? ИИ будет выключен; после проверки включите его снова.'))return;
    try{await api('/ai/settings/opencode-credentials','POST',{});form.elements.enabled.checked=false;output.textContent='Ключ провайдера передан. Проверьте соединение, затем сохраните настройки с включённым ИИ.';}catch(error){output.textContent=error.message;}
  };
  const workerStatus=host.querySelector('#ai-worker-status');
  async function loadWorkers(){
    try{const workers=await api('/ai/settings/workers');const list=host.querySelector('#ai-worker-list');list.replaceChildren();
      for(const worker of workers){const row=document.createElement('p');row.textContent=`Пользователь ${worker.ownerUserId}: ${worker.url} · ${worker.provider} · ${worker.credentialReady?'ключ настроен':'ключ не настроен'} `;
        const button=document.createElement('button');button.type='button';button.textContent=worker.enabled?'Отключить':'Включить';button.onclick=async()=>{try{await api(`/ai/settings/workers/${worker.ownerUserId}/enabled?enabled=${!worker.enabled}`,'PUT',{});await loadWorkers();}catch(error){workerStatus.textContent=error.message;}};
        const inspect=document.createElement('button');inspect.type='button';inspect.textContent='Состояние';
        const stop=document.createElement('button');stop.type='button';stop.textContent='Остановить worker';
        const runtime=document.createElement('span');
        inspect.onclick=async()=>{inspect.disabled=true;try{const state=await api(`/ai/settings/workers/${worker.ownerUserId}/runtime`);runtime.textContent=state.phase==='Manual'?' · Управление вручную: контроллер не подключён':` · ${state.phase} · активных операций: ${state.activeLeases} · простой: ${state.idleSeconds} с`;}catch(error){runtime.textContent=error.message;}finally{inspect.disabled=false;}};
        stop.onclick=async()=>{if(!confirm('Остановить простаивающий worker? История и ключ сохранятся; следующий запрос запустит его автоматически, если настроен контроллер.'))return;stop.disabled=true;try{const state=await api(`/ai/settings/workers/${worker.ownerUserId}/stop`,'POST',{});runtime.textContent=' · '+state.phase;}catch(error){runtime.textContent=error.message;}finally{stop.disabled=false;}};
        row.append(button,inspect,stop,runtime);list.append(row);}
    }catch(error){workerStatus.textContent=error.message;}
  }
  host.querySelector('#ai-workers-refresh').onclick=loadWorkers;
  host.querySelector('#ai-worker-form').onsubmit=async event=>{
    event.preventDefault();const fields=event.target.elements;const button=event.target.querySelector('button');button.disabled=true;
    try{await api('/ai/settings/workers','POST',{ownerUserId:Number(fields.ownerUserId.value),url:fields.url.value.trim(),password:fields.password.value,provider:fields.provider.value.trim()});workerStatus.textContent='Назначено. Пользователь может настроить личный ключ.';await loadUsers();await loadWorkers();}
    catch(error){workerStatus.textContent=error.message;}finally{fields.password.value='';button.disabled=false;}
  };
  applyVisibility();
  await loadUsers();
  await loadWorkers();
  if(sharedMode() && passwordConfigured) await loadCatalog(true);
  await mountAiChat(host);
}

function aiChatMarkup(test = false) {
  return `<div class="card"><h2>${test ? "Проверка чата" : "ИИ-помощник"}</h2><p>Проверка отправляет текст выбранной модели и расходует квоту провайдера. Не вводите пароли и персональные данные.</p>
    <details id="ai-personal" hidden><summary>Мой ключ провайдера OpenCode</summary><p>Ключ будет передан только выделенному вам worker. Закройте остальные разговоры перед заменой. Ключ не отправляется сообщением модели.</p>
      <form id="ai-personal-form"><label>Личный API-ключ<input name="apiKey" type="password" autocomplete="new-password" maxlength="4096" required></label><button type="submit">Сохранить личный ключ</button></form>
      <button type="button" id="ai-personal-delete">Отозвать и удалить ключ</button><p id="ai-personal-status" role="status"></p></details>
    <label>Модель для нового разговора<select id="ai-chat-model"></select></label><button id="ai-new" type="button">Новый разговор</button><button id="ai-stop" type="button">Остановить</button>
    <details id="ai-diagnostics" hidden><summary>Диагностика только для чтения</summary><div class="toolbar">
      <label>ПК<select id="ai-diagnostic-machine"></select></label>
      <label>Проверка<select id="ai-diagnostic-tool"><option value="computer.status">Состояние ПК (данные сервера)</option><option value="network.http.server">Сайт с сервера</option><option value="network.http.client">Сайт с выбранного ПК</option></select></label>
      <label>Сайт<select id="ai-diagnostic-target"></select></label><button id="ai-run-check" type="button">Проверить</button></div>
      <p id="ai-diagnostic-report" style="white-space:pre-wrap"></p><label><input id="ai-attach-check" type="checkbox" checked> Приложить последний отчёт к следующему вопросу (до 5 минут)</label></details>
    <div id="ai-chat-log" aria-live="polite" style="white-space:pre-wrap;max-height:420px;overflow:auto"></div>
    <form id="ai-chat-form"><label>Сообщение<textarea name="message" maxlength="8000" required></textarea></label><button type="submit">Отправить</button></form>
    <details><summary>Моя история разговоров</summary><button id="ai-load-archive" type="button">Загрузить историю</button><div id="ai-archive"></div></details></div>`;
}

async function renderAiChatPage(){
  const host=document.querySelector("#content");if(!aiSecureOrigin()){aiHttpsNotice(host);return;}host.innerHTML=aiChatMarkup();await mountAiChat(host);
}

async function mountAiChat(host) {
  const chatForm=host.querySelector('#ai-chat-form');
  let session=null, controller=null, diagnosticController=null, diagnosticId=null;
  const log=host.querySelector('#ai-chat-log');
  function line(text){const item=document.createElement('p');item.textContent=text;log.append(item);item.scrollIntoView({block:'nearest'});return item;}
  let openingSession=null;
  const ensureSession=async()=>{
    if(session)return session;
    if(!openingSession)openingSession=api('/ai/sessions','POST',{windowInstanceId:crypto.randomUUID(),modelId:host.querySelector('#ai-chat-model').value||null})
      .then(value=>{if(!chatForm.isConnected){api('/ai/sessions/'+encodeURIComponent(value.id)+'/close','POST',{}).catch(()=>{});throw new Error('Окно закрыто');}session=value;return value;}).finally(()=>{openingSession=null;});
    return openingSession;
  };
  const close=async()=>{controller?.abort();diagnosticController?.abort();diagnosticId=null;if(session){const id=session.id;session=null;try{await api('/ai/sessions/'+encodeURIComponent(id)+'/close','POST',{});}catch{}}};
  host.querySelector('#ai-new').onclick=async()=>{if(openingSession)return;await close();log.textContent='';line('Новый разговор.');};
  host.querySelector('#ai-stop').onclick=()=>{controller?.abort();if(session)api('/ai/sessions/'+encodeURIComponent(session.id)+'/cancel','POST',{}).catch(()=>{});};
  host.querySelector('#ai-chat-form').onsubmit=async event=>{
    event.preventDefault();if(controller||diagnosticController)return;
    const input=event.target.elements.message;const text=input.value.trim();if(!text)return;
    const submit=event.target.querySelector('button');submit.disabled=true;controller=new AbortController();
    line('Вы: '+text);input.value='';const reply=line('Подключение…');
    try{
      await ensureSession();
      const response=await fetch('/api/ai/sessions/'+encodeURIComponent(session.id)+'/messages',{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify({clientMessageId:crypto.randomUUID(),text,diagnosticId:host.querySelector('#ai-attach-check').checked?diagnosticId:null}),signal:controller.signal});
      if(!response.ok)throw new Error((await response.json()).error||'Ошибка запроса');
      const reader=response.body.getReader(),decoder=new TextDecoder();let pending='',answer='',completed=false;reply.textContent='';
      while(true){const part=await reader.read();if(part.done)break;pending+=decoder.decode(part.value,{stream:true});let newline;while((newline=pending.indexOf('\n'))>=0){const event=JSON.parse(pending.slice(0,newline));pending=pending.slice(newline+1);if(event.type==='delta'){answer+=event.text;reply.textContent=answer;}if(event.type==='completed'){reply.textContent=event.text;completed=true;}if(event.type==='sources')for(const source of event.sources){const button=document.createElement('button');button.type='button';button.textContent='Источник: '+source.title;button.onclick=async()=>{try{const article=await api('/ai/knowledge/'+source.id);line(article.source.title+'\n'+article.text);}catch(error){line(error.message);}};log.append(button);}if(event.type==='usage')line('Токены: '+(event.usage.inputTokens??'?')+' / '+(event.usage.outputTokens??'?')+' · Стоимость: '+(event.usage.cost??'не сообщена'));if(event.type==='error')throw new Error(event.text);}}
      if(!completed)throw new Error('Ответ прерван. Повторная отправка не выполнялась.');
    }catch(error){reply.textContent+=(reply.textContent?'\n':'')+(error.name==='AbortError'?'Остановлено.':error.message);}
    finally{controller=null;submit.disabled=false;}
  };
  try{
    const caps=await api('/ai/capabilities');
    if(caps.personalCredentials){
      host.querySelector('#ai-personal').hidden=false;
      const state=await api('/ai/credentials');const status=host.querySelector('#ai-personal-status');
      status.textContent=state.available?`${state.provider}: ${state.configured?'ключ настроен':'введите ключ'}`:'Администратор ещё не назначил или отключил ваш worker.';
      host.querySelector('#ai-personal-form button').disabled=!state.available;
      host.querySelector('#ai-personal-delete').disabled=!state.assigned;
      host.querySelector('#ai-personal-form').onsubmit=async event=>{event.preventDefault();const input=event.target.elements.apiKey;const button=event.target.querySelector('button');button.disabled=true;
        try{await close();await api('/ai/credentials','PUT',{apiKey:input.value});status.textContent='Ключ сохранён. Откройте ИИ-чат заново для обновления доступных моделей.';}
        catch(error){status.textContent=error.message;}finally{input.value='';button.disabled=false;}};
      host.querySelector('#ai-personal-delete').onclick=async()=>{if(!confirm('Отозвать личный ключ и остановить ваши текущие разговоры?'))return;
        try{await close();const result=await api('/ai/credentials','DELETE');status.textContent=result.removed?'Ключ удалён. Обновите страницу.':'Личный доступ выключен. Worker недоступен: повторите удаление после восстановления связи.';}
        catch(error){status.textContent=error.message;}};
    }
    const model=host.querySelector('#ai-chat-model');model.innerHTML=(caps.models||[]).map(m=>`<option value="${escape(m.id)}">${escape(m.id)} · ${escape(m.price)}</option>`).join('');model.value=caps.defaultModel||'';
    if(!caps.enabled)line(caps.pilotEligible===false?'ИИ доступен только участникам пилота.':caps.personalCredentials?'ИИ недоступен. Проверьте личный ключ и назначение worker; администратор также может выключить ИИ.':'ИИ выключен администратором.');
    if(caps.diagnostics){const choices=await api('/ai/diagnostics/options');host.querySelector('#ai-diagnostics').hidden=false;
      host.querySelector('#ai-diagnostic-machine').innerHTML=choices.computers.map(c=>`<option value="${escape(c.machineName)}">${escape(c.machineName)} · ${c.isOnline?'Online':'Offline'}</option>`).join('');
      host.querySelector('#ai-diagnostic-target').innerHTML=choices.targets.map(t=>`<option value="${escape(t.id)}">${escape(t.title)}</option>`).join('');
    }
  }catch{ /* The settings page remains usable while the chat is disabled. */ }
  host.querySelector('#ai-run-check').onclick=async()=>{
    if(controller||diagnosticController)return;const button=host.querySelector('#ai-run-check'),report=host.querySelector('#ai-diagnostic-report');button.disabled=true;diagnosticController=new AbortController();diagnosticId=null;report.textContent='Проверка…';
    try{await ensureSession();const response=await fetch('/api/ai/sessions/'+encodeURIComponent(session.id)+'/diagnostics',{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},signal:diagnosticController.signal,body:JSON.stringify({clientRequestId:crypto.randomUUID(),machineName:host.querySelector('#ai-diagnostic-machine').value,tool:host.querySelector('#ai-diagnostic-tool').value,targetId:host.querySelector('#ai-diagnostic-target').value})});const check=await response.json();if(!response.ok)throw new Error(check.error||'Ошибка проверки');report.textContent=check.status+'\n'+JSON.stringify(JSON.parse(check.resultJson),null,2);if(check.status==='Completed')diagnosticId=check.id;}catch(error){report.textContent=error.message;}finally{diagnosticController=null;button.disabled=false;}
  };
  host.querySelector('#ai-load-archive').onclick=async()=>{
    const archive=host.querySelector('#ai-archive');archive.textContent='Загрузка…';
    try{const sessions=await api('/ai/sessions');archive.textContent='';for(const item of sessions){const row=document.createElement('div'),open=document.createElement('button'),remove=document.createElement('button');open.textContent=new Date(item.createdAt*1000).toLocaleString()+' · '+item.backend+' · '+item.modelId+' · '+item.status;remove.textContent='Удалить';const body=document.createElement('div');open.onclick=async()=>{try{const messages=await api('/ai/sessions/'+encodeURIComponent(item.id)+'/messages');body.textContent=messages.map(m=>'Вы: '+m.text+'\nИИ: '+m.reply+'\n'+m.status).join('\n\n');body.style.whiteSpace='pre-wrap';}catch(error){body.textContent=error.message;}};remove.onclick=async()=>{if(!confirm('Удалить этот разговор из истории?'))return;try{await api('/ai/sessions/'+encodeURIComponent(item.id),'DELETE');row.remove();}catch(error){body.textContent=error.message;}};row.append(open,remove,body);archive.append(row);}}catch(error){archive.textContent=error.message;}
  };
  const heartbeat=setInterval(()=>{if(session&&chatForm.isConnected)api('/ai/sessions/'+encodeURIComponent(session.id)+'/heartbeat','POST',{}).catch(()=>{});},60000);
  // Navigation must abort its request and close the conversation as well.
  const observer=new MutationObserver(()=>{if(!chatForm.isConnected){observer.disconnect();clearInterval(heartbeat);close();}});
  observer.observe(document.querySelector('#content'),{childList:true,subtree:true});
}

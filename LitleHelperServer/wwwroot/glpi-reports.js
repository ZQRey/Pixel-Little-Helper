"use strict";
let glpiReportData, glpiReportFilters = {}, glpiReportTab = 'tickets', glpiReportPage = 0;
let surveyRefreshTimer, activeSurveyId = null;
let reportTimeZone='Asia/Qyzylorda', reportLocale='ru-KZ', reportContextReady=false;
async function ensureReportContext() {
  if(reportContextReady)return;
  const context=await api('/reports/glpi/context');
  // Validate before applying server configuration to Intl and date presets.
  new Intl.DateTimeFormat(context.locale,{timeZone:context.timeZone}).format(new Date());
  reportTimeZone=context.timeZone;reportLocale=context.locale;reportContextReady=true;
}
const reportSort = {};
function reportSortColumns(tab) {
  return tab === 'tickets'
    ? [t => t.id, t => t.requesters.map(a => a.name).join('; ') || null, t => t.responsible?.name ?? null, t => t.status,
       t => t.executionSeconds, t => t.lifetimeSeconds, t => t.effortSeconds, t => t.status === 6 ? t.currentScore : null]
    : [p => p.name, p => p.total, p => p.closed, p => p.meanSeconds, p => p.meanScore, p => p.rated];
}
function sortedReportRows(rows, tab) {
  const sort = reportSort[tab];
  if (!sort) return rows;
  const value = reportSortColumns(tab)[sort.column];
  const compare = (a, b) => typeof a === 'number' && typeof b === 'number' ? a - b : String(a).localeCompare(String(b), 'ru', { numeric: true, sensitivity: 'base' });
  return [...rows].sort((a, b) => {
    const av = value(a), bv = value(b);
    if (av == null || bv == null) return av == null && bv == null ? 0 : av == null ? 1 : -1;
    const primary = compare(av, bv);
    const secondary = !primary && tab === 'technicians' && sort.column === 1 ? compare(a.responsible, b.responsible) : 0;
    return (primary || secondary) * (sort.desc ? -1 : 1);
  });
}
function reportColumnHeader(label, column) {
  const sort = reportSort[glpiReportTab], active = sort?.column === column;
  const arrow = active ? sort.desc ? ' ↓' : ' ↑' : ' ↕';
  return reportColumnHelp(label).replace(label, `<button type="button" class="report-sort-button" data-report-sort="${column}" title="Сортировать: ${label}">${label}<span aria-hidden="true">${arrow}</span></button>`);
}
function bindReportSorting() {
  document.querySelectorAll('#report-rows [data-report-sort]').forEach(button => {
    const column = Number(button.dataset.reportSort), sort = reportSort[glpiReportTab];
    button.closest('th').setAttribute('aria-sort', sort?.column === column ? sort.desc ? 'descending' : 'ascending' : 'none');
    button.closest('th').addEventListener('click', e => {
      if (e.target.closest('.report-help')) return;
      const previous = reportSort[glpiReportTab];
      const text = glpiReportTab === 'tickets' ? column === 1 || column === 2 : column === 0;
      reportSort[glpiReportTab] = { column, desc: previous?.column === column ? !previous.desc : !text };
      glpiReportPage = 0; closeReportHints(); renderReportRows();
      document.querySelector(`#report-rows [data-report-sort="${column}"]`)?.focus();
    });
  });
}
const reportTime = s => s == null ? 'Нет данных' : s < 60 ? Number(s).toLocaleString('ru-RU', { maximumFractionDigits: 1 }) + ' с' : s < 3600 ? Math.round(s / 60) + ' мин' : (s / 3600).toLocaleString('ru-RU', { maximumFractionDigits: 1 }) + ' ч';
const reportScore = s => s == null ? 'Нет оценок' : Number(s).toFixed(2) + ' / 5';
const reportDay = d => new Intl.DateTimeFormat('sv-SE', { timeZone: reportTimeZone }).format(d);
const reportDate = d => d ? new Date(d).toLocaleString(reportLocale, { timeZone: reportTimeZone }) : '—';
const reportStatus = s => ({ 1: 'Новая', 2: 'В работе', 3: 'Запланирована', 4: 'Ожидание', 5: 'Решена', 6: 'Закрыта' })[s] || s;
const reportPeriodDate = value => value ? value.split('-').reverse().join('.') : '—';
function reportComparisonView(data) {
  const c=data.comparison; if(!c)return '';
  const period=(a,b)=>reportPeriodDate(a)+' — '+reportPeriodDate(b);
  const value=(m,v)=>v==null?'Нет данных':m.unit==='seconds'?reportTime(v):m.unit==='score'?reportScore(v):Number(v).toLocaleString('ru-RU');
  const delta=m=>m.delta==null?'Нет данных':(m.delta>0?'+':m.delta<0?'−':'')+(m.unit==='seconds'?reportTime(Math.abs(m.delta)):Math.abs(m.delta).toLocaleString('ru-RU',{maximumFractionDigits:2}))+(m.unit==='score'?' балл.':'');
  const change=m=>m.delta==null?'Недостаточно данных':m.unit==='score'?'В баллах':m.percent==null?(m.unit==='count'?'Ранее не было':'База сравнения — 0'):(m.percent>0?'+':'')+m.percent.toLocaleString('ru-RU',{maximumFractionDigits:1})+'%';
  return `<details class="report-comparison"><summary>Сравнение периодов</summary><div class="report-comparison-body"><p>${escape(c.rule)}</p>${c.warning?`<p class="report-warning">${escape(c.warning)}</p>`:''}${c.metrics?.length?table(['Показатель','Текущий: '+period(c.currentFrom,c.currentTo),'Сравнение: '+period(c.previousFrom,c.previousTo),'Разница','Изменение'],c.metrics.map(m=>`<tr><td>${escape(m.label)}</td><td>${escape(value(m,m.current))}${m.currentSamples!=null?`<small>N=${m.currentSamples}</small>`:''}</td><td>${escape(value(m,m.previous))}${m.previousSamples!=null?`<small>N=${m.previousSamples}</small>`:''}</td><td>${escape(delta(m))}</td><td>${escape(change(m))}</td></tr>`).join('')):''}<p class="report-note">${escape(c.note)}</p></div></details>`;
}
const reportHints = {
  'Заявки в выборке': ['total', '<p>Количество заявок, которые соответствуют выбранному периоду и всем фильтрам отчёта.</p><p>Поле «Отбирать по дате» определяет, какое событие должно попасть в период: создание, решение, закрытие или получение оценки.</p><p>Например, при отборе по дате создания учитываются созданные в этот период заявки, даже если их решили позднее. Удалённые заявки в выборку не входят.</p>'],
  'Закрыты сейчас': ['closed', '<p>Количество заявок из текущей выборки, которые на момент последнего обновления данных имеют статус «Закрыта».</p><p>Это не обязательно заявки, закрытые в выбранный период: период применяется к событию, указанному в поле «Отбирать по дате».</p><p>Чтобы увидеть закрытые за период заявки, выберите отбор по дате закрытия. Статус «Решена» ещё не означает «Закрыта».</p>'],
  'Среднее исполнения': ['mean', '<p>Среднее время от принятия заявки до её закрытия: сумма длительностей, делённая на количество закрытых заявок с корректными датами принятия и закрытия.</p><p>Учитываются заявки текущей выборки. Незакрытые заявки, заявки без даты принятия или закрытия и с некорректной последовательностью дат исключаются; число наблюдений указано под показателем.</p><p>Считается календарное время, включая ночи, выходные и ожидание. Несколько очень долгих заявок могут заметно увеличить среднее. Это не фактические трудозатраты специалиста.</p>'],
  'Оценка результата': ['score', '<p>Средняя пользовательская оценка по шкале от 1 до 5 для закрытых заявок текущей выборки. Рядом указано количество ответов, участвующих в расчёте.</p><p>Заявки без оценки не снижают среднее. «Нет оценок» означает отсутствие подходящих ответов, а не нулевой результат.</p><p>Оценка должна относиться к текущему закрытию: старый ответ не переносится на повторное закрытие. При малом числе ответов выводы о качестве работы могут быть ненадёжными.</p>'],
  'Медиана / P90': ['resolution', '<p><b>Медиана</b> — время от принятия до закрытия, быстрее которого исполнена половина заявок.</p><p><b>P90</b> — время, за которое исполнено примерно 90% заявок. Остальные 10% потребовали больше времени.</p><p>Например, «1,4 ч / 55,6 ч»: половина заявок исполнена примерно за 1 ч 24 мин, а 90% — за 55 ч 36 мин.</p><p>Считается календарное время, включая ночи, выходные и ожидание. Незакрытые заявки и заявки без корректных дат принятия и закрытия исключаются из расчёта.</p>'],
  'Трудозатраты': ['effort', '<p>Суммарное время фактической работы, записанное специалистами в задачах GLPI или в поле длительности заявки.</p><p>Например, заявка могла ждать решения два дня, а работа над ней заняла 30 минут. В трудозатратах учитываются эти 30 минут.</p><p>«Нет данных» означает, что длительность работы не заполнена. Это не нулевая работа. Показатель не рассчитывается из времени между созданием и решением заявки.</p>']
};
function reportColumnHelp(label) {
  const specialist = glpiReportTab === 'technicians';
  const hints = {
    'Участие / результат': '<p><b>Участие</b> — число заявок, где сотрудник назначен исполнителем или определён ответственным за результат.</p><p><b>Результат</b> — число заявок, где сотрудник определён ответственным, включая ещё не закрытые. Ответственный — автор последнего неотклонённого решения; если его определить нельзя — единственный назначенный специалист. Сервисные записи исключаются.</p><p>Например, 12 / 10: участие в 12 заявках, ответственность за 10. Одна заявка может учитываться в участии нескольких специалистов. Применяются период и фильтры отчёта.</p>',
    'Закрыто': '<p>Число заявок текущей выборки со статусом «Закрыта», за результат которых отвечает сотрудник. Статус «Решена» ещё не считается закрытием.</p><p>Закрытие уже учтённой заявки увеличивает это число, но не обязательно меняет «Участие / результат». Период применяется к событию из поля «Отбирать по дате».</p>',
    'Среднее исполнения': '<p>Среднее календарное время от принятия до закрытия ' + (specialist ? 'заявок, за результат которых отвечает специалист.' : 'заявок данного заявителя.') + '</p><p>Учитываются только закрытые заявки текущей выборки с корректными датами принятия и закрытия. N — число заявок в расчёте. Ночи, выходные и ожидание включаются; фактические трудозатраты считаются отдельно.</p>',
    'Оценка': glpiReportTab === 'tickets' ? '<p>Пользовательская оценка результата этой заявки по шкале от 1 до 5. Учитывается ответ, относящийся к текущему закрытию.</p><p>«Нет оценок» означает отсутствие подходящего ответа; прочерк — заявка сейчас не закрыта. Старый ответ не переносится на повторное закрытие.</p>' : '<p>Средняя пользовательская оценка от 1 до 5 по закрытым заявкам, за результат которых отвечает специалист. Учитываются только оценки текущего закрытия.</p><p>Заявки без оценки не снижают среднее. При менее 5 ответах отображается «Мало данных»: выборка недостаточна для уверенного сравнения сотрудников.</p>',
    'Ответов': '<p>Число пользовательских оценок, участвующих в расчёте средней оценки специалиста. Это ответы на опрос, а не комментарии или сообщения в заявках.</p><p>Под числом показана доля оценённых заявок среди закрытых заявок, за результат которых отвечает специалист. При отсутствии закрытых заявок доля не рассчитывается.</p>',
    'Время исполнения': '<p>Календарное время от принятия заявки до её закрытия. Рассчитывается только для закрытой заявки с корректными датами принятия и закрытия.</p><p>Включает ночи, выходные и ожидание. «Нет данных» означает, что заявка ещё не закрыта либо нужные даты отсутствуют или некорректны. Это не фактическое время работы специалиста.</p>',
    'Время жизни': '<p>Календарное время от создания заявки до её закрытия, включая время до принятия, ожидание, ночи и выходные.</p><p>Рассчитывается только для закрытых заявок с корректными датами. Иначе отображается «Нет данных».</p>',
    'Работа': reportHints['Трудозатраты'][1]
  };
  const enabled = glpiReportTab === 'tickets' ? ['Время исполнения', 'Время жизни', 'Работа', 'Оценка'] : specialist ? ['Участие / результат', 'Закрыто', 'Среднее исполнения', 'Оценка', 'Ответов'] : ['Среднее исполнения'];
  if (!enabled.includes(label)) return label;
  const id = 'report-column-hint-' + enabled.indexOf(label);
  return `${label} <span class="report-help"><button type="button" class="report-help-button" data-report-help aria-label="Что означает ${label}" aria-expanded="false" aria-controls="${id}">?</button><span id="${id}" class="report-help-popup" role="region" aria-label="Пояснение: ${label}" hidden>${hints[label]}</span></span>`;
}
let activeReportColumnHint = null;
function positionReportColumnHint() {
  if (!activeReportColumnHint) return;
  const {button,hint} = activeReportColumnHint;
  const rect = button.getBoundingClientRect();
  const viewport = window.visualViewport;
  let left = (viewport?.offsetLeft || 0) + 12, top = (viewport?.offsetTop || 0) + 12;
  let right = left + (viewport?.width || document.documentElement.clientWidth) - 24;
  let bottom = top + (viewport?.height || window.innerHeight) - 24;
  // GLPI can grow its iframe to the full report height. Use the visible parent
  // viewport too, so a popup near a table remains on screen after page scrolling.
  try {
    if (window.frameElement) {
      const frame = window.frameElement.getBoundingClientRect();
      left = Math.max(left, 12 - frame.left); right = Math.min(right, window.parent.innerWidth - frame.left - 12);
      top = Math.max(top, 110 - frame.top); bottom = Math.min(bottom, window.parent.innerHeight - frame.top - 12);
    }
  } catch (_) { /* Cross-origin embeds use their own viewport. */ }
  const width = Math.max(0,Math.min(420,right-left));
  Object.assign(hint.style, {position:'fixed',width:width+'px',boxSizing:'border-box',right:'auto',bottom:'auto',margin:'0',left:Math.max(left,Math.min(rect.left,right-width))+'px',top:top+'px',maxHeight:Math.max(0,bottom-top)+'px',overflowY:'auto',overflowX:'hidden'});
  const height = hint.getBoundingClientRect().height;
  const preferred = rect.bottom + 8 + height <= bottom ? rect.bottom + 8 : rect.top - height - 8;
  hint.style.top = Math.max(top,Math.min(preferred,bottom-height))+'px';
}
function bindReportColumnHints() {
  document.querySelectorAll('#report-rows [data-report-help]').forEach(button => button.addEventListener('click', () => {
    const open = button.getAttribute('aria-expanded') !== 'true';
    closeReportHints(); button.setAttribute('aria-expanded', String(open));
    const hint = document.getElementById(button.getAttribute('aria-controls'));
    hint.hidden = !open;
    if (open) {
      activeReportColumnHint = {button,hint,parent:hint.parentNode,next:hint.nextSibling};
      hint.classList.add('report-column-popup');
      document.body.appendChild(hint);
      positionReportColumnHint();
    }
  }));
}
function closeReportHints(focus=false) {
  if (activeReportColumnHint) {
    const {button,hint,parent,next} = activeReportColumnHint;
    hint.hidden = true; button.setAttribute('aria-expanded','false');
    hint.classList.remove('report-column-popup'); hint.removeAttribute('style');
    if (parent.isConnected) parent.insertBefore(hint,next?.parentNode === parent ? next : null); else hint.remove();
    activeReportColumnHint = null;
    if (focus && button.isConnected) button.focus();
  }
  document.querySelectorAll('[data-report-help][aria-expanded="true"]').forEach(button => { button.setAttribute('aria-expanded','false'); const hint=document.getElementById(button.getAttribute('aria-controls')); if(hint)hint.hidden=true; if(focus)button.focus(); });
}
document.addEventListener('click', e => { if(!e.target.closest('.report-help, .report-column-popup'))closeReportHints(); });
document.addEventListener('keydown', e => { if(e.key==='Escape')closeReportHints(true); });
document.addEventListener('scroll',positionReportColumnHint,{capture:true,passive:true});
window.addEventListener('resize',positionReportColumnHint);
try { if (window.parent !== window) window.parent.addEventListener('scroll',positionReportColumnHint,{passive:true}); } catch (_) {}
function reportPeriod(kind) {
  glpiReportFilters.period = kind;
  const today = reportDay(new Date()), end = new Date(today + 'T12:00:00Z'), start = new Date(end);
  if (kind === 'week') start.setUTCDate(start.getUTCDate() - (start.getUTCDay() + 6) % 7);
  if (kind === 'month') start.setUTCDate(1);
  if (kind === 'year') { start.setUTCMonth(0); start.setUTCDate(1); }
  glpiReportFilters.from = start.toISOString().slice(0, 10); glpiReportFilters.to = today;
}
function reportReactionChart(data) {
  const buckets = data.reaction || [], max = Math.max(1, ...buckets.map(b => b.count));
  return `<div class="panel report-chart report-reaction"><h3>Время реакции на заявку</h3><p>От создания до принятия в работу. Интервалы не пересекаются, верхняя граница включается.</p><div class="report-reaction-bars">${buckets.map(b => `<button type="button" class="report-reaction-bar ${String(data.filter.reaction) === String(b.id) ? 'selected' : ''}" data-reaction="${b.id}" aria-pressed="${String(data.filter.reaction) === String(b.id)}" aria-label="${escape(b.range)}: ${b.count} заявок" title="${escape(b.range)}: ${b.count} заявок"><strong>${b.count}</strong><span class="report-reaction-track"><i data-height="${Math.max(b.count ? 4 : 0, b.count / max * 145)}"></i></span><span>${escape(b.label)}</span></button>`).join('')}</div><p class="report-note">Без корректных дат создания и принятия: ${data.reactionMissing ?? 0}. Нажмите на столбец, чтобы отобрать заявки. Распределение показано до отбора по времени реакции.</p>${data.filter.reaction != null ? `<div class="report-reaction-selection">Выбран интервал: ${escape(buckets.find(b => b.id === data.filter.reaction)?.range || '')}. Сводка, таблицы и выгрузка учитывают этот отбор. <button type="button" id="report-reaction-clear">Сбросить интервал</button></div>` : ''}</div>`;
}
async function selectReportReaction(value) {
  if (value == null) delete glpiReportFilters.reaction; else glpiReportFilters.reaction = String(value);
  glpiReportTab = 'tickets'; glpiReportPage = 0;
  await renderGlpiReports();
  $('#report-rows')?.scrollIntoView({block:'start',behavior:'smooth'});
}
async function renderGlpiReports() {
  await ensureReportContext();
  if (!glpiReportFilters.from) reportPeriod('month');
  const viewGeneration = generation;
  const data = await api('/reports/glpi?' + new URLSearchParams(glpiReportFilters));
  if (viewGeneration !== generation || page !== 'reports') return;
  glpiReportData = data;
  const c = data.choices, s = data.summary;
  closeReportHints();
  const choice = (items, value) => '<option value="">Все</option>' + items.map(x => `<option value="${x.id}" ${String(x.id) === String(value) ? 'selected' : ''}>${escape(x.name)}</option>`).join('');
  $('#stats').hidden = true;
  $('#content').innerHTML = `<div class="report-banner"><div><span class="eyebrow">GLPI / АНАЛИТИКА</span><h2>Как работает поддержка</h2><p>Обращения, время решения и обратная связь пользователей.</p></div><div class="report-freshness">Срез: ${reportDate(data.syncedAt)}<br>История с ${reportDate(data.coverageFrom)}<br>${data.totalAvailable} заявок в доступной области</div></div>
  ${data.error ? `<p class="report-warning">${escape(data.error)}</p>` : ''}
  <form id="report-filter" class="report-filters"><input type="hidden" name="period" value="${escape(data.filter.periodKind || 'auto')}"><div class="report-presets">${[['day', 'День'], ['week', 'Неделя'], ['month', 'Месяц'], ['year', 'Год']].map(([k, v]) => `<button type="button" data-period="${k}">${v}</button>`).join('')}</div>
  <label>С даты<input name="from" type="date" required value="${escape(data.filter.from)}"></label><label>По дату<input name="to" type="date" required value="${escape(data.filter.to)}"></label>
  <label>Отбирать по дате<select name="basis">${[['created', 'Создания'], ['solved', 'Решения'], ['closed', 'Закрытия'], ['answered', 'Оценки']].map(([k, v]) => `<option value="${k}" ${data.filter.basis === k ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
  <label>Филиал<select name="branchId">${choice(c.branches, glpiReportFilters.branchId)}</select></label><label>Специалист<select name="technician">${choice(c.technicians, glpiReportFilters.technician)}</select></label>
  <label>Заявитель<select name="requester">${choice(c.requesters, glpiReportFilters.requester)}</select></label><label>Статус<select name="status"><option value="">Все</option>${[1,2,3,4,5,6].map(n => `<option value="${n}" ${String(glpiReportFilters.status) === String(n) ? 'selected' : ''}>${reportStatus(n)}</option>`).join('')}</select></label>
  <label>Номер или тема<input name="search" value="${escape(glpiReportFilters.search || '')}" maxlength="200" placeholder="Найти заявку"></label>
  <label>Сравнить с<select name="compare" id="report-compare-mode">${[['previous','Предыдущий сопоставимый период'],['custom','Выбрать даты'],['none','Не сравнивать']].map(([k,v])=>`<option value="${k}" ${data.filter.comparison===k?'selected':''}>${v}</option>`).join('')}</select></label>
  <label data-compare-custom ${data.filter.comparison==='custom'?'':'hidden'}>Сравнение с<input name="compareFrom" type="date" value="${escape(data.filter.compareFrom || data.comparison?.previousFrom || '')}" ${data.filter.comparison==='custom'?'required':'disabled'}></label>
  <label data-compare-custom ${data.filter.comparison==='custom'?'':'hidden'}>Сравнение по<input name="compareTo" type="date" value="${escape(data.filter.compareTo || data.comparison?.previousTo || '')}" ${data.filter.comparison==='custom'?'required':'disabled'}></label><button class="primary">Показать</button></form>
  <div class="report-kpis">${[
    ['Заявки в выборке', s.total, 'Основа: ' + ({created:'созданы',solved:'решены',closed:'закрыты',answered:'оценены'})[data.filter.basis]],
    ['Закрыты сейчас', s.closed, 'Из выбранных заявок'], ['Среднее исполнения', reportTime(s.meanSeconds), s.executionSamples + ' наблюдений'],
    ['Медиана / P90', reportTime(s.medianSeconds) + ' / ' + reportTime(s.p90Seconds), 'Календарное время'],
    ['Оценка результата', reportScore(s.meanScore), s.rated + ' ответов'], ['Трудозатраты', reportTime(s.effortSeconds), s.effortSamples ? 'Заполнено: ' + s.effortSamples : 'Учёт ещё не вёлся']
  ].map(([a,b,d]) => { const hint=reportHints[a]; return `<div class="report-kpi"><div class="report-kpi-label">${a}${hint ? ` <div class="report-help"><button type="button" class="report-help-button" data-report-help aria-label="Что означает ${a}" aria-expanded="false" aria-controls="report-hint-${hint[0]}">?</button><div id="report-hint-${hint[0]}" class="report-help-popup" role="region" aria-label="Пояснение: ${a}" hidden>${hint[1]}</div></div>` : ''}</div><strong>${b}</strong><span>${d}</span></div>`; }).join('')}</div>
  ${reportComparisonView(data)}
  <div class="report-charts"><div class="panel report-chart"><h3>Динамика обращений</h3><p><span class="report-dot created"></span> Созданы &nbsp; <span class="report-dot solved"></span> Решены &nbsp; <span class="report-dot closed"></span> Закрыты</p><div id="report-timeline"></div></div><div class="panel report-chart"><h3>Оценки пользователей</h3><div id="report-stars-chart"></div><p class="report-note">Неоценённые заявки не снижают среднюю оценку. При менее 5 ответах сравнивать сотрудников рано.</p></div></div>
  ${reportReactionChart(data)}
  <div class="toolbar"><div class="report-tabs">${[['tickets', 'Заявки'], ['technicians', 'Специалисты'], ['requesters', 'Заявители']].map(([k,v]) => `<button data-report-tab="${k}" class="${glpiReportTab === k ? 'primary' : ''}">${v}</button>`).join('')}</div><span class="report-spacer"></span>${can('reports.glpi.export') ? '<button data-export="xlsx">↓ XLSX</button><button data-export="pdf">↓ PDF</button>' : ''}${can('reports.glpi.settings') ? '<button id="report-sync">Обновить из GLPI</button><button id="report-settings">Опросы</button>' : ''}</div><div id="report-rows"></div>${surveyReportHtml(data.surveyReport)}
  <p class="report-note">Текущая очередь во всей доступной области: ${data.currentOpen}. Отсутствует заявитель: ${s.missingRequester}; не определён ответственный: ${s.ambiguousResponsible}. Участие нескольких исполнителей может пересекаться. Исторический ответственный восстановлен по данным GLPI. Время исполнения: принята → закрыта; время жизни: создана → закрыта. При отсутствии дат — «Нет данных». Это календарные интервалы, а не личные трудозатраты. Часовой пояс: ${escape(reportTimeZone)}.</p>`;
  document.querySelectorAll('[data-report-help]').forEach(button => button.addEventListener('click', () => { const open=button.getAttribute('aria-expanded')!=='true'; closeReportHints(); button.setAttribute('aria-expanded',String(open)); document.getElementById(button.getAttribute('aria-controls')).hidden=!open; }));
  $('#report-compare-mode').addEventListener('change',e=>{const custom=e.target.value==='custom';document.querySelectorAll('[data-compare-custom]').forEach(label=>{label.hidden=!custom;const input=label.querySelector('input');input.disabled=!custom;input.required=custom;});});
  $('#report-filter').querySelectorAll('[name=from],[name=to]').forEach(input=>input.addEventListener('change',()=>{$('#report-filter [name=period]').value='custom';}));
  $('#report-filter').addEventListener('submit', e => { e.preventDefault(); const reaction=glpiReportFilters.reaction; glpiReportFilters = Object.fromEntries([...new FormData(e.target)].filter(([, v]) => v !== '')); if(reaction != null)glpiReportFilters.reaction=reaction; glpiReportPage = 0; renderGlpiReports().catch(e => toast(e.message, true)); });
  document.querySelectorAll('[data-reaction]').forEach(b => b.addEventListener('click', () => selectReportReaction(b.dataset.reaction).catch(e => toast(e.message,true))));
  $('#report-reaction-clear')?.addEventListener('click', () => selectReportReaction(null).catch(e => toast(e.message,true)));
  document.querySelectorAll('[data-period]').forEach(b => b.addEventListener('click', () => { reportPeriod(b.dataset.period); glpiReportPage = 0; renderGlpiReports().catch(e => toast(e.message, true)); }));
  document.querySelectorAll('[data-survey-exempt]').forEach(b=>b.addEventListener('click',()=>{
    modal('Исключить требование оценки',`<form id="survey-exempt-form"><p>Оценка не будет подставлена. Причина останется в журнале и отчёте.</p><label>Причина<textarea name="reason" maxlength="500" required></textarea></label><button class="primary">Исключить</button><p id="survey-exempt-error" class="error"></p></form>`);
    $('#survey-exempt-form').addEventListener('submit',async e=>{e.preventDefault();const button=e.target.querySelector('button');button.disabled=true;try{await api('/ticket-surveys/'+b.dataset.surveyExempt+'/exempt','POST',{reason:new FormData(e.target).get('reason')});closeModal();await renderGlpiReports();}catch(e){$('#survey-exempt-error').textContent=e.message;}finally{button.disabled=false;}});
  }));
  document.querySelectorAll('[data-report-tab]').forEach(b => b.addEventListener('click', () => { glpiReportTab = b.dataset.reportTab; glpiReportPage = 0; renderReportRows(); document.querySelectorAll('[data-report-tab]').forEach(x => x.classList.toggle('primary', x.dataset.reportTab === glpiReportTab)); }));
  document.querySelectorAll('[data-export]').forEach(b => b.addEventListener('click', async () => { b.disabled = true; try {
    const query = new URLSearchParams({ ...glpiReportFilters, snapshot: data.snapshot || '' });
    const path = '/reports/glpi/export/' + b.dataset.export + '?' + query;
    const r = await fetch(typeof reportExportUrl === 'function' ? reportExportUrl(path) : '/api' + path, { headers: token ? { Authorization: 'Bearer ' + token } : {} });
    if (!r.ok) throw new Error((await r.json()).error || 'Не удалось создать файл');
    const blob = await r.blob(), url = URL.createObjectURL(blob), a = document.createElement('a'); a.href = url; a.download = `GLPI-${data.filter.from}-${data.filter.to}.${b.dataset.export}`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (e) { toast(e.message, true); } finally { b.disabled = false; } }));
  $('#report-sync')?.addEventListener('click', async e => { e.target.disabled = true; try { await api('/reports/glpi/sync', 'POST', {}); await renderGlpiReports(); } catch (e) { toast(e.message, true); } finally { if ($('#report-sync')) $('#report-sync').disabled = false; } });
  $('#report-settings')?.addEventListener('click', reportSettings);
  if(can('reports.glpi.settings')) {
    const button=document.createElement('button');button.textContent='Организация';button.id='report-organization';
    button.addEventListener('click',reportOrganizationSettings);$('#report-settings')?.after(button);
  }
  const points = data.timeline, max = Math.max(1, ...points.flatMap(p => [p.created,p.solved,p.closed]));
  $('#report-timeline').innerHTML = points.length ? `<div class="report-bars">${points.map(p => `<button class="report-bar-day" data-day="${p.day}" title="${p.day}: создано ${p.created}, решено ${p.solved}, закрыто ${p.closed}"><span class="report-bar-group">${['created','solved','closed'].map(k => `<i class="${k}" data-height="${Math.max(p[k] ? 3 : 0, p[k] / max * 105)}"></i>`).join('')}</span><small>${p.day.slice(5)}</small></button>`).join('')}</div><small>Нажмите на дату для детализации. Динамика учитывает даты каждого события.</small>` : '<div class="empty">За этот период событий нет</div>';
  $('#report-timeline').querySelectorAll('[data-day]').forEach(b => b.addEventListener('click', () => { glpiReportFilters.from = glpiReportFilters.to = b.dataset.day; glpiReportFilters.period='day'; glpiReportTab = 'tickets'; glpiReportPage = 0; renderGlpiReports().catch(e => toast(e.message,true)); }));
  const scores = [5,4,3,2,1].map(n => ({ n, count: data.tickets.filter(t => t.status === 6 && t.currentScore != null && Math.round(t.currentScore) === n).length })), maxScores = Math.max(1,...scores.map(x => x.count));
  $('#report-stars-chart').innerHTML = scores.map(x => `<div class="report-rating-row"><span>${x.n} ★</span><div><i data-width="${x.count / maxScores * 100}"></i></div><b>${x.count}</b></div>`).join('');
  document.querySelectorAll('[data-height]').forEach(el => { el.style.height=el.dataset.height+'px'; });
  document.querySelectorAll('[data-width]').forEach(el => { el.style.width=el.dataset.width+'%'; });
  renderReportRows();
}
function renderReportRows() {
  closeReportHints();
  const rows = sortedReportRows(glpiReportData[glpiReportTab], glpiReportTab), slice = rows.slice(glpiReportPage * 50, glpiReportPage * 50 + 50);
  const ticketTable = glpiReportTab !== 'tickets' ? '' : table(['Заявка', 'Заявитель', 'Ответственный', 'Статус', 'Время исполнения', 'Время жизни', 'Работа', 'Оценка'].map(reportColumnHeader), slice.map(t => `<tr><td><button class="quiet report-link" data-ticket-report="${t.id}">#${t.id} · ${escape(t.title)}</button><small>${reportDate(t.createdAt)}</small></td><td>${escape(t.requesters.map(a => a.name).join('; ') || 'Не указан')}</td><td>${escape(t.responsible?.name || 'Команда / не определён')}<small>${escape(t.responsible?.profile || '')}</small></td><td><span class="pill">${reportStatus(t.status)}</span></td><td>${reportTime(t.executionSeconds)}</td><td>${reportTime(t.lifetimeSeconds)}</td><td>${reportTime(t.effortSeconds)}</td><td>${t.status === 6 ? reportScore(t.currentScore) : '—'}</td></tr>`).join(''));
  $('#report-rows').innerHTML = glpiReportTab === 'tickets' ? ticketTable : table(['Сотрудник', glpiReportTab === 'technicians' ? 'Участие / результат' : 'Создано', 'Закрыто', 'Среднее исполнения', 'Оценка', 'Ответов'].map(reportColumnHeader), slice.map(p => `<tr><td><strong>${escape(p.name)}</strong><small>${escape(p.login)} · ${escape(p.profile)}</small></td><td>${p.total}${glpiReportTab === 'technicians' ? ' / ' + p.responsible : ''}</td><td>${p.closed}</td><td>${reportTime(p.meanSeconds)}<small>N=${p.samples}</small></td><td>${reportScore(p.meanScore)}<small>${p.sufficient ? 'Достаточная выборка' : 'Мало данных'}</small></td><td>${p.rated}<small>${p.responsePercent == null ? '—' : p.responsePercent.toFixed(0) + '% от закрытых'}</small></td></tr>`).join(''));
  $('#report-rows').insertAdjacentHTML('beforeend', `<div class="toolbar"><small>Всего: ${rows.length} · страница ${glpiReportPage + 1} из ${Math.max(1,Math.ceil(rows.length / 50))}</small><button id="report-prev" ${glpiReportPage ? '' : 'disabled'}>←</button><button id="report-next" ${(glpiReportPage + 1) * 50 < rows.length ? '' : 'disabled'}>→</button></div>`);
  bindReportColumnHints();
  bindReportSorting();
  $('#report-prev').addEventListener('click', () => { glpiReportPage--; renderReportRows(); }); $('#report-next').addEventListener('click', () => { glpiReportPage++; renderReportRows(); });
  document.querySelectorAll('[data-ticket-report]').forEach(b => b.addEventListener('click', () => { const t = rows.find(t => t.id === Number(b.dataset.ticketReport)); modal('Заявка #' + t.id, `<h3>${escape(t.title)}</h3><p>Заявители: ${escape(t.requesters.map(a => a.name).join('; '))}</p><p>Регистрация: ${escape(t.registrationDescription || t.registeredBy?.name || 'Не указана')}</p><p>Назначены: ${escape(t.assignees.map(a => a.name).join('; '))}</p><p>Ответственный: ${escape(t.responsible?.name || 'Не определён')}</p><p>${escape(t.attribution)}</p><p>Создана: ${reportDate(t.createdAt)}<br>Принята: ${reportDate(t.acceptedAt)}<br>Решена: ${reportDate(t.solvedAt)}<br>Закрыта: ${reportDate(t.closedAt)}</p><p>До принятия: ${reportTime(t.acceptanceSeconds)}<br>До решения: ${reportTime(t.resolutionSeconds)}<br>Время исполнения (принята → закрыта): ${reportTime(t.executionSeconds)}<br>Время жизни (создана → закрыта): ${reportTime(t.lifetimeSeconds)}<br>Работа: ${reportTime(t.effortSeconds)}</p><p>Оценка: ${t.status === 6 ? reportScore(t.currentScore) : 'Результат не закрыт'}</p><a target="_blank" rel="noopener" href="${escape(t.url)}">Открыть в GLPI →</a>`); }));
}
async function reportSettings() {
  await ensureReportContext();
  const settings = await api('/reports/glpi/settings'), p = settings.policy || {};
  modal('Оценка результата заявки', `<form id="survey-settings"><p>Ответ сохраняется в GLPI. Срок оценки — строго 14 дней после закрытия.</p>
    <label><input type="checkbox" name="enabled" ${settings.surveysEnabled?'checked':''}> Опросы включены</label>
    <label><input type="checkbox" name="mandatory" ${p.mandatory?'checked':''}> Обязательная оценка новых закрытий</label>
    <p>При включении обязательного режима прежние закрытия остаются добровольными. Отсрочки и сроки считаются по рабочим дням, понедельник–пятница, ${escape(reportTimeZone)}.</p>
    <label>Филиалы пилота (ID через запятую; пусто — все)<input name="branches" value="${escape((p.branchIds||[]).join(','))}"></label>
    <label>Напоминание через рабочих минут<input type="number" name="reminder" min="30" max="1440" value="${p.reminderMinutes||120}" required></label>
    <label>Отсрочек за рабочий день<input type="number" name="deferrals" min="0" max="10" value="${p.dailyDeferrals??2}" required></label>
    <label>Срок ответа, рабочих часов<input type="number" name="due" min="1" max="90" value="${p.dueBusinessHours||9}" required></label>
    <label>Начало рабочего дня<input type="number" name="start" min="0" max="23" value="${p.workStart??9}" required></label>
    <label>Конец рабочего дня<input type="number" name="end" min="1" max="24" value="${p.workEnd??18}" required></label>
    <fieldset><legend>Уведомления ответственным администраторам</legend>
    <label><input type="checkbox" name="adminAlerts" ${p.adminAlertsEnabled!==false?'checked':''}> Уведомлять о просрочке, недоставке и ошибках записи</label>
    <label>Получатели<select name="alertRecipients" multiple>${[...(settings.recipients||[]),...(p.adminUserIds||[]).filter(id=>!(settings.recipients||[]).some(u=>u.id===id)).map(id=>({id,fullName:'Недоступный получатель',username:'ID '+id}))].map(u=>`<option value="${u.id}" ${(p.adminUserIds||[]).includes(u.id)?'selected':''}>${escape(u.fullName||u.username)} · ${escape(u.username)}</option>`).join('')}</select></label>
    <small>Пустой выбор — все действующие пользователи с правами просмотра и настройки статистики. Каждый видит только свои доступные заявки.</small>
    <label>Повтор сводки через рабочих минут<input type="number" name="alertRepeat" min="60" max="10080" value="${p.adminRepeatMinutes||240}" required></label>
    <label>Считать непоказанный опрос недоставленным через рабочих часов<input type="number" name="undeliveredHours" min="1" max="90" value="${p.undeliveredBusinessHours||9}" required></label>
    </fieldset><button class="primary">Сохранить</button><p id="survey-error" class="error"></p></form>`);
  $('#survey-settings').addEventListener('submit',async e=>{e.preventDefault();const f=new FormData(e.target),button=e.target.querySelector('button');button.disabled=true;
    try { await api('/reports/glpi/settings','POST',{enabled:f.has('enabled'),policy:{mandatory:f.has('mandatory'),branchIds:String(f.get('branches')).split(',').map(v=>v.trim()).filter(Boolean).map(Number),reminderMinutes:Number(f.get('reminder')),dailyDeferrals:Number(f.get('deferrals')),dueBusinessHours:Number(f.get('due')),workStart:Number(f.get('start')),workEnd:Number(f.get('end')),adminAlertsEnabled:f.has('adminAlerts'),adminUserIds:f.getAll('alertRecipients').map(Number),adminRepeatMinutes:Number(f.get('alertRepeat')),undeliveredBusinessHours:Number(f.get('undeliveredHours'))}});closeModal();await renderGlpiReports(); }
    catch(e){$('#survey-error').textContent=e.message;}finally{button.disabled=false;}});
}
async function reportOrganizationSettings() {
  try {
    const settings=await api('/reports/glpi/organization'),p=settings.pending;
    modal('Организация отчётности',`<form id="report-organization-form">
      <p>Настройки действуют для этого backend GLPI. После сохранения требуется плановый перезапуск сервера помощника; текущие отчёты продолжат использовать прежние значения.</p>
      <label>Название<input name="name" maxlength="255" value="${escape(p.organizationName)}"></label>
      <label>Часовой пояс IANA<input name="zone" required value="${escape(p.timeZoneId)}"></label>
      <label>Локаль<input name="locale" required value="${escape(p.locale)}"></label>
      <label>ID сервисных пользователей (через запятую)<input name="services" value="${escape(p.serviceUserIds.join(','))}"></label>
      <label>Сервисные логины (через запятую)<input name="logins" value="${escape(p.serviceLogins.join(','))}"></label>
      <label>ID профилей специалистов (через запятую)<input name="profiles" value="${escape(p.technicianProfileIds.join(','))}"></label>
      <p>Пустой список профилей сохраняет историческое определение по Technician / Super-Admin. Для новой организации явно укажите профили.</p>
      <button class="primary">Сохранить для следующего запуска</button></form>`);
    $('#report-organization-form').addEventListener('submit',async e=>{
      e.preventDefault();const f=new FormData(e.target);
      const ids=name=>String(f.get(name)).split(',').map(s=>s.trim()).filter(Boolean).map(Number);
      try {await api('/reports/glpi/organization','POST',{name:f.get('name'),timeZone:f.get('zone'),locale:f.get('locale'),serviceUserIds:ids('services'),serviceLogins:String(f.get('logins')).split(',').map(s=>s.trim()).filter(Boolean),technicianProfileIds:ids('profiles')});closeModal();toast('Сохранено. Применение — при плановом перезапуске сервера помощника.');}
      catch(error){toast(error.message,true);}
    });
  }catch(error){toast(error.message,true);}
}
const surveyState = t => ({Pending:t.dueAt&&new Date(t.dueAt)<new Date()?'Просрочена':'Ожидает оценки',Writing:'Ожидает записи GLPI',Answered:'Оценена',Expired:'Истекли 14 дней',Blocked:'Ошибка записи',Cancelled:'Отменена',Exempt:'Исключена'}[t.state]||t.state);
async function renderTicketSurveys() {
  await ensureReportContext();
  if(me.authSource !== 'AD') { $('#content').innerHTML='<div class="empty">Войдите под своей доменной учётной записью.</div>';return; }
  const current=generation,rows=await api('/ticket-surveys/pending');if(generation!==current||page!=='surveys')return;
  $('#content').innerHTML=rows.length?rows.map(t=>`<div class="panel report-survey"><span class="eyebrow">ЗАЯВКА #${t.ticketId} ЗАКРЫТА</span><h3>${escape(t.title)}</h3><p>${escape(t.specialist)}</p><p>${t.mandatory?'Обязательная оценка · ':''}${surveyState(t)}</p><small>Оценить можно до ${reportDate(t.expiresAt)}${t.dueAt?' · Ответ ожидается '+reportDate(t.dueAt):''}</small>${t.error?`<p>${escape(t.error)}</p>`:''}${t.state!=='Writing'?`<button class="primary" data-survey="${escape(t.id)}">Оценить результат ★</button>`:'<p>Ответ принят помощником. Повторно отвечать не нужно.</p>'}</div>`).join(''):'<div class="empty">Нет заявок, ожидающих вашей оценки.</div>';
  document.querySelectorAll('[data-survey]').forEach(b=>b.addEventListener('click',()=>surveyForm(rows.find(t=>t.id===b.dataset.survey))));
  clearInterval(surveyRefreshTimer);
  surveyRefreshTimer=setInterval(async()=>{
    if(page!=='surveys'){clearInterval(surveyRefreshTimer);return;}
    try {
      if(!$('#modal').open)await renderTicketSurveys();
      else if(activeSurveyId && !(await api('/ticket-surveys/pending')).some(i=>i.id===activeSurveyId&&i.state==='Pending')){closeModal();activeSurveyId=null;await renderTicketSurveys();}
    }catch(_){/* Keep the visible queue while reconnecting. */}
  },30000);
}
async function surveyForm(t) {
  const claim=await api('/ticket-surveys/'+t.id+'/claim','POST',{});if(!claim.claimed){toast('Опрос уже открыт на другом устройстве или изменился. Обновите список.',true);return;}
  t=(await api('/ticket-surveys/pending')).find(i=>i.id===t.id);if(!t)return;
  activeSurveyId=t.id;
  const canDefer=!t.mandatory||t.deferralsLeft>0&&new Date(t.dueAt)>new Date();
  modal('Оцените результат · #'+t.ticketId,`<form id="survey-answer"><h3>${escape(t.title)}</h3><p>${escape(t.specialist)}</p><p>${t.mandatory?'Оценка обязательна. ':''}Срок — 14 дней после закрытия.</p><div class="report-star-input" role="radiogroup" aria-label="Оценка от 1 до 5">${[1,2,3,4,5].map(n=>`<label><input type="radio" name="score" value="${n}" required><span>${n} ★</span></label>`).join('')}</div><label>Комментарий (необязательно)<textarea name="comment" maxlength="2000"></textarea></label><button class="primary">Отправить оценку</button><div class="actions">${canDefer?'<button type="button" data-snooze="30">На 30 минут</button><button type="button" data-snooze="120">На 2 часа</button>':''}${!t.mandatory?'<button type="button" id="survey-dismiss">Не напоминать</button>':''}</div><p id="survey-error" class="error"></p></form>`);
  let requestId=crypto.randomUUID();
  $('#survey-answer').addEventListener('submit',async e=>{e.preventDefault();const button=e.target.querySelector('button');button.disabled=true;try{const f=Object.fromEntries(new FormData(e.target));const result=await api('/ticket-surveys/'+t.id+'/answer','POST',{score:Number(f.score),comment:f.comment,requestId});if(!result.saved&&!result.queued){requestId=crypto.randomUUID();throw new Error(result.error||'Ошибка записи GLPI');}closeModal();toast(result.saved?'Оценка сохранена в GLPI. Спасибо.':'Оценка принята помощником и ожидает записи в GLPI.');if(page==='surveys')await renderTicketSurveys();}catch(e){$('#survey-error').textContent=e.message;}finally{button.disabled=false;}});
  document.querySelectorAll('[data-snooze]').forEach(b=>b.addEventListener('click',async()=>{try{await api('/ticket-surveys/'+t.id+'/snooze','POST',{minutes:Number(b.dataset.snooze)});closeModal();}catch(e){toast(e.message,true);}}));
  $('#survey-dismiss')?.addEventListener('click',async()=>{try{await api('/ticket-surveys/'+t.id+'/dismiss','POST',{});closeModal();}catch(e){toast(e.message,true);}});
}
function surveyReportHtml(report) {
  if(!report)return '';
  return `<details class="panel report-survey-control"><summary>Обязательные оценки: ${report.answered} из ${report.eligible} · ${report.responsePercent==null?'—':report.responsePercent.toFixed(0)+'%'} ответов</summary><p>Ожидают: ${report.pending} · Просрочены: ${report.overdue} · Не показаны: ${report.undelivered} · Запись GLPI: ${report.writing} · Ошибки: ${report.blocked} · Истекли 14 дней: ${report.expired} · Исключены: ${report.exempt}</p><p>Среднее время до оценки: ${reportTime(report.meanAnswerSeconds)}. Период и фильтры совпадают с отчётом. Истечение срока не создаёт оценку.</p>${table(['Заявка','Заявитель','Исполнитель','Состояние','Ответ ожидается','Оценить до'],report.rows.filter(i=>i.mandatory).map(i=>`<tr><td>#${i.ticketId} · ${escape(i.title)}</td><td>${escape(i.requester)}</td><td>${escape(i.specialist)}</td><td>${surveyState(i)}${i.error?'<small>'+escape(i.error)+'</small>':''}</td><td>${reportDate(i.dueAt)}</td><td>${reportDate(i.expiresAt)}${can('reports.glpi.settings')&&['Pending','Blocked'].includes(i.state)?`<button class="quiet" data-survey-exempt="${escape(i.id)}">Исключить с причиной</button>`:''}</td></tr>`).join(''))}</details>`;
}

let surveyAdminTimer;
async function pollSurveyAdminAlerts() {
  let banner=document.getElementById('survey-admin-alert');
  if(!token || !me || !can('reports.glpi.settings') || !can('reports.glpi.view')){if(banner)banner.hidden=true;return;}
  const userId=me.id,authToken=token;
  let notice;
  try{notice=await api('/reports/glpi/alerts');}catch(e){if(banner)banner.hidden=true;throw e;}
  if(!me || me.id!==userId || token!==authToken)return;
  if(!notice || !notice.unread){if(banner)banner.hidden=true;return;}
  if(!banner){banner=document.createElement('aside');banner.id='survey-admin-alert';banner.className='panel';banner.setAttribute('role','status');document.querySelector('#workspace main header').after(banner);}
  const d=notice.attention;
  banner.innerHTML=`<strong>Оценки заявок требуют внимания: ${d.total}</strong><p>Просрочены: ${d.overdue} · Не показаны заявителю: ${d.undelivered} · Нет учётной записи AD в Helper: ${d.unmapped} · Ошибки записи: ${d.blocked}</p><button data-survey-attention-open>Открыть контроль оценок</button><button data-survey-attention-read>Прочитано</button>`;
  banner.hidden=false;
  banner.querySelector('[data-survey-attention-open]').addEventListener('click',async()=>{
    page='reports';await loadPage();const details=document.querySelector('.report-survey-control');if(details){details.open=true;details.scrollIntoView({block:'start',behavior:'smooth'});}
  });
  banner.querySelector('[data-survey-attention-read]').addEventListener('click',async()=>{
    try{await api('/reports/glpi/alerts/read','POST',{revision:notice.revision});await pollSurveyAdminAlerts();}catch(e){toast(e.message,true);}
  });
}
function startSurveyAdminAlerts(){
  clearInterval(surveyAdminTimer);pollSurveyAdminAlerts().catch(()=>{});
  surveyAdminTimer=setInterval(()=>pollSurveyAdminAlerts().catch(()=>{}),60000);
}

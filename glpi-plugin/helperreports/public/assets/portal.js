"use strict";
const $ = s => document.querySelector(s);
const escape = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let generation = 0, page = 'reports', token = null;
const can = key => key === 'reports.glpi.export';
const reportExportUrl = path => 'data.php?' + new URLSearchParams({path});
async function api(path, method='GET') {
  if(method !== 'GET') throw new Error('Изменение настроек доступно в панели помощника');
  const response = await fetch(reportExportUrl(path), {credentials:'same-origin'});
  const result = await response.json().catch(()=>null);
  if(!response.ok || !result) throw new Error(result?.error || 'Сеанс GLPI завершён или отчёт недоступен. Обновите страницу GLPI.');
  return result;
}
function table(head, rows) { return `<div class="panel"><div class="table-wrap"><table><thead><tr>${head.map(h=>`<th>${h}</th>`).join('')}</tr></thead><tbody>${rows || `<tr><td colspan="${head.length}">Пока нет записей</td></tr>`}</tbody></table></div></div>`; }
function toast(text,error=false) { $('#toast').textContent=text; $('#toast').className=error?'error':''; $('#toast').hidden=false; clearTimeout(toast.timer); toast.timer=setTimeout(()=>$('#toast').hidden=true,8000); }
function modal(title,html) {
  $('#modal-title').textContent=title; $('#modal-content').innerHTML=html;
  const rect=window.frameElement?.getBoundingClientRect();
  // A GLPI sticky header must not cover a dialog opened after scrolling the table.
  const top=rect ? Math.max(20,110-rect.top) : 30;
  $('#modal').style.top=top+'px'; $('#modal').style.maxHeight=Math.max(300,(window.parent.innerHeight||800)-160)+'px';
  $('#modal').showModal();
}
function closeModal() { $('#modal').close(); }
$('#modal-close').addEventListener('click',closeModal);
$('#refresh-button').addEventListener('click',()=>renderGlpiReports().catch(e=>toast(e.message,true)));
renderGlpiReports().catch(e=>{ $('#content').textContent=e.message; });

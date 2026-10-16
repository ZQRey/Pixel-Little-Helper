"use strict";
(() => {
  const script=document.currentScript;
  if(!script)return;
  const base=new URL('../front/',script.src);
  const ready=async()=>{
    try{
      const response=await fetch(new URL('survey-status.php',base),{credentials:'same-origin',cache:'no-store'});
      if(!response.ok)return;
      const rows=await response.json();if(!Array.isArray(rows)||!rows.length)return;
      const banner=document.createElement('aside');banner.className='alert alert-info';banner.setAttribute('role','status');
      const link=document.createElement('a');link.href=new URL('surveys.php',base);link.textContent='Ожидают вашей оценки: '+rows.length+' · Оценить результат ★';banner.append(link);
      (document.querySelector('main')||document.body).prepend(banner);
      document.querySelectorAll('form').forEach(form=>{
        if(!form.querySelector('[name="add"]')||!form.querySelector('[name="content"],textarea'))return;
        const label=document.createElement('label');label.className='d-block alert alert-warning';
        const box=document.createElement('input');box.type='checkbox';box.name='_helper_survey_urgent';box.value='1';
        label.append(box,document.createTextNode(' Срочное обращение — требуется помощь до оценки предыдущей заявки'));form.append(label);
      });
    }catch(_){/* Support remains accessible during integration outages. */}
  };
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',ready);else ready();
})();

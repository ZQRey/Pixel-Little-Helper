const {chromium}=require('playwright'),fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1380,height:950}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async r=>{
   const url=new URL(r.request().url());
   if(url.pathname.startsWith('/api/messenger/')){
    const name=url.pathname.slice('/api/messenger/'.length);
    let data=name==='me'?{id:1,fullName:'Тестовый пользователь',username:'test'}:name==='users'?[{id:2,fullName:'Анна Иванова',username:'anna',isActive:true,isOnline:true,unread:0}]:name.startsWith('extras/')?{}:[];
    return r.fulfill({json:data});
   }
   if(url.pathname.startsWith('/messengerHub'))return r.fulfill({status:503,body:''});
   if(process.env.FORMATTING_LIVE==='1')return r.continue();
   const relative=url.pathname==='/messenger/'?'messenger/index.html':url.pathname.slice(1);
   const file=path.resolve('LitleHelperServer/wwwroot',relative);
   if(!file.startsWith(path.resolve('LitleHelperServer/wwwroot')+path.sep))return r.fulfill({status:404});
   if(fs.existsSync(file))return r.fulfill({path:file});
   return r.fulfill({status:404,body:''});
  });
  await page.goto('https://helper.gp1.loc/messenger/');
  await page.locator('#contacts .contact').filter({hasText:'Анна Иванова'}).click();
  const input=page.locator('#input');
  await input.fill('один\nдва\nтри');
  await input.evaluate(el=>el.setSelectionRange(5,8));
  await input.press('Control+b');
  assert.equal(await input.inputValue(),'один\n**два**\nтри');
  assert.equal(await page.locator('.formatting-preview span').filter({hasText:/^два$/}).evaluate(el=>getComputedStyle(el).fontWeight),'700');
  await input.press('Control+z');assert.equal(await input.inputValue(),'один\nдва\nтри');
  await input.evaluate(el=>el.setSelectionRange(5,8));
  assert.equal(await page.locator('.formatting-toolbar').count(),0);
  await input.press('Control+Alt+e');
  assert.equal(await input.inputValue(),'один\n[align=center]два[/align]\nтри');
  await input.press('Control+Alt+Shift+r');
  assert.equal(await page.locator('.formatting-preview .formatted-line').evaluateAll(rows=>rows.every(el=>getComputedStyle(el).textAlign==='right')),true);
  await input.fill('<img src=x onerror=alert(1)>');
  assert.equal(await page.locator('.formatting-preview img').count(),0);
  await page.locator('#settings').click();
  await page.getByRole('button',{name:'Помощь стилизации',exact:true}).click();
  assert.match(await page.locator('#modal-body').innerText(),/Ctrl\+B/);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#chat-title').innerText(),'Анна Иванова');
  await input.fill('**Сохранённый черновик**');
  await input.press('Escape');
  assert.equal(await page.locator('#app').isVisible(),true);
  assert.equal(await page.locator('#chat-title').innerText(),'Выберите сотрудника');
  await page.locator('#contacts .contact').filter({hasText:'Анна Иванова'}).click();
  assert.equal(await input.inputValue(),'**Сохранённый черновик**');
  // Verify actual history rendering shares styling, alignment and escaping.
  const rendered=await page.evaluate(()=>{
   const text=richText('[align=center]**Жирный**[/align]\n`<script>x</script>`');
   document.getElementById('feed').replaceChildren(text);
   return{alignment:getComputedStyle(text.children[0]).textAlign,bold:getComputedStyle(text.children[0].firstElementChild).fontWeight,script:text.querySelectorAll('script').length,code:text.querySelector('code').textContent};
  });
  assert.deepEqual(rendered,{alignment:'center',bold:'700',script:0,code:'<script>x</script>'});
  await page.screenshot({path:'backups/message-formatting-browser.png'});
  assert.deepEqual(errors,[]);
  console.log('PASS Edge: hotkeys, preview, undo, selected/all alignment, literal HTML, help, Esc, draft restoration and message rendering; API fully mocked');
 }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1;});

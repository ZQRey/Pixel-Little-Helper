const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
vm.runInThisContext(fs.readFileSync('LitleHelperServer/wwwroot/messenger/message-formatting.js','utf8'));
const f=HelperFormatting;
for(const c of JSON.parse(fs.readFileSync('tests/message-formatting-cases.json','utf8')))test(c.name,()=>assert.deepEqual(f.parse(c.text),c.expected));
test('toggle formatting and preserve word selection',()=>{
 let edit=f.toggle('слово',0,5,'**');assert.deepEqual(edit,{text:'**слово**',start:2,length:5});
 assert.equal(f.toggle(edit.text,edit.start,edit.length,'**').text,'слово');
 edit=f.toggle(edit.text,edit.start,edit.length,'*');assert.equal(edit.text,'***слово***');
 assert.equal(f.toggle(edit.text,edit.start,edit.length,'*').text,'**слово**');
});
test('caret insertion and size limits',()=>{
 assert.deepEqual(f.toggle('',0,0,'**'),{text:'****',start:2,length:0});
 assert.throws(()=>f.toggle('a'.repeat(4000),0,4000,'**'),/4000/);
});
test('alignment selected line, whole message, reset and trailing newline',()=>{
 assert.equal(f.align('один\nдва\nтри',5,1,'center').text,'один\n[align=center]два[/align]\nтри');
 assert.equal(f.align('a\nb\nc',0,2,'right').text,'[align=right]a[/align]\nb\nc');
 const edit=f.align('a\nb',1,0,'right',true);
 assert.equal(edit.text,'[align=right]a[/align]\n[align=right]b[/align]');
 assert.equal(f.align(edit.text,0,0,'left',true).text,'a\nb');
 assert.throws(()=>f.align('a',0,1,'justify'),/выравнивание/);
});
test('shortcuts leave paste and unrelated keys untouched',()=>{
 assert.equal(f.shortcut('b',true,false),'**');assert.equal(f.shortcut('e',true,true),'```');
 assert.equal(f.shortcut('v',true,false),null);assert.equal(f.shortcut('b',false,false),null);
});

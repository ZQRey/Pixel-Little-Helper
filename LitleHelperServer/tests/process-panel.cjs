const fs = require('fs'), vm = require('vm'), assert = require('assert');
const source = fs.readFileSync(require('path').join(__dirname, '../wwwroot/app.js'), 'utf8');
const elements = new Map();
function element(id) {
  if (!elements.has(id)) elements.set(id, {value:'', innerHTML:'', textContent:'', disabled:false, isConnected:true, open:true, style:{}, handlers:{}, addEventListener(event, fn){this.handlers[event]=fn;}});
  return elements.get(id);
}
const entries = [
  {pid:101,name:'Alpha<script>',memoryBytes:1048576,startTimeUtcTicks:'638900000000000001',canStop:true},
  {pid:777,name:'Beta',memoryBytes:null,startTimeUtcTicks:'638900000000000002',canStop:true},
  {pid:4,name:'System',memoryBytes:null,startTimeUtcTicks:null,canStop:false}
];
const list = element('#process-list');
list.querySelectorAll = () => [...list.innerHTML.matchAll(/data-pid="(\d+)"/g)].map(match => ({dataset:{pid:match[1]},addEventListener(event,fn){element('radio-'+match[1]).handlers[event]=fn;}}));
let requests = 0;
const context = vm.createContext({
  $: element, modal(){}, toast(){}, confirm(){return false;}, Date, JSON, Number, Promise, setTimeout,
  api: async (path,method,body) => {
    if(path==='/commands'){assert.equal(body.type,'processes');requests++;return [{taskId:'task-1'}];}
    assert.equal(path,'/tasks/task-1');return {status:'Completed',exitCode:0,result:JSON.stringify(entries)};
  }
});
const escapeLine = source.split('\n').find(line => line.startsWith('const escape ='));
const tableLine = source.split('\n').find(line => line.startsWith('function table('));
vm.runInContext(escapeLine+'\n'+tableLine+'\n'+source.slice(source.indexOf('async function waitProcessTask')), context);
(async () => {
  await vm.runInContext("processForm({machineName:'PC-TEST',isOnline:true})",context);
  assert.equal(requests,1);assert(list.innerHTML.includes('Alpha&lt;script&gt;'));assert(!list.innerHTML.includes('Alpha<script>'));
  element('#process-filter').value='ALPHA';element('#process-filter').handlers.input();
  assert(list.innerHTML.includes('data-pid="101"'));assert(!list.innerHTML.includes('data-pid="777"'));
  element('radio-101').handlers.change();assert.equal(element('#process-stop').disabled,false);
  element('#process-filter').value='777';element('#process-filter').handlers.input();
  assert(list.innerHTML.includes('data-pid="777"'));assert(!list.innerHTML.includes('data-pid="101"'));assert.equal(element('#process-stop').disabled,true);
  element('radio-777').handlers.change();await element('#process-stop').handlers.click();assert.equal(requests,1,'cancelled stop must not dispatch');
  await element('#process-refresh').handlers.click();assert.equal(requests,2);assert.equal(element('#process-stop').disabled,true);
  console.log('PASS process panel: loading, escaped names, name/PID filtering, selection reset, cancelled stop, refresh');
})().catch(error => {console.error(error);process.exitCode=1;});

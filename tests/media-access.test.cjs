
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const helper=fs.readFileSync('LitleHelperServer/wwwroot/messenger/media-access.js','utf8');
function context(url='https://helper.gp1.loc/messenger/call.html',secure=true,media={getUserMedia:async()=>{}}){
 const redirects=[];
 const c={URL,location:{href:url,replace:u=>redirects.push(u)},window:{isSecureContext:secure},navigator:{mediaDevices:media}};
 vm.createContext(c);vm.runInContext(helper,c);return {c,redirects,api:c.window.HelperMedia};
}
test('HTTP upgrades to HTTPS preserving call identity, query and fragment',()=>{
 const {redirects}=context('http://helper.gp1.loc/messenger/call.html?peer=5&token=test#call',false);
 assert.deepEqual(redirects,['https://helper.gp1.loc/messenger/call.html?peer=5&token=test#call']);
});
test('HTTPS and loopback do not redirect',()=>{
 assert.equal(context().redirects.length,0);
 assert.equal(context('http://localhost:5000/messenger/',true).redirects.length,0);
});
test('insecure context produces actionable error instead of missing camera',()=>{
 assert.throws(()=>context(undefined,false).api.requireDevices(),/HTTPS/);
});
test('denied, busy and missing devices produce different explanations',()=>{
 const {api}=context();
 assert.match(api.explain({name:'NotAllowedError'},'Камера'),/доступ запрещён/);
 assert.match(api.explain({name:'NotReadableError'},'Микрофон'),/занято/);
 assert.match(api.explain({name:'NotFoundError'},'Камера'),/не найдено/);
});
test('device list requests microphone permission and releases temporary stream',async()=>{
 let authorized=false,stopped=false;
 const {api}=context(undefined,true,{
  enumerateDevices:async()=>authorized?[{kind:'audioinput',label:'USB mic',deviceId:'usb'}]:[],
  getUserMedia:async()=>{authorized=true;return{getTracks:()=>[{stop:()=>{stopped=true;}}]};}
 });
 const devices=await api.inputs();
 assert.equal(devices[0].label,'USB mic');assert.equal(stopped,true);
});
test('permission denial while enumerating is propagated',async()=>{
 const error=Object.assign(new Error('denied'),{name:'NotAllowedError'});
 const {api}=context(undefined,true,{enumerateDevices:async()=>[],getUserMedia:async()=>{throw error;}});
 await assert.rejects(()=>api.inputs(),{name:'NotAllowedError'});
});
for(const page of ['call','messenger']){
 test(page+': real capture keeps camera while denied microphone is reported despite silent fallback',async()=>{
  const notices=[],video={kind:'video'},silent={kind:'audio'};
  const error=Object.assign(new Error('denied'),{name:'NotAllowedError'});
  const {c,api}=context(undefined,true,{getUserMedia:async constraints=>{
   if(constraints.audio)throw error;
   return{getVideoTracks:()=>[video]};
  }});
  c.HelperMedia=api;c.console={warn:()=>{}};
  c.MediaStream=class{constructor(tracks){this.tracks=tracks;}};
  c.getAudioConstraints=()=>true;c.notify=o=>notices.push(o.body);
  c.showToast=s=>notices.push(s);c.params=new URLSearchParams();
  c.localStorage={getItem:()=>null};c.callState={isVideo:true};c.localVideo={};
  c.window.AudioContext=class{
   createOscillator(){return{connect(){},start(){}};}
   createGain(){return{gain:{value:1},connect(){}};}
   createMediaStreamDestination(){return{stream:{getAudioTracks:()=>[silent]}};}
  };
  const source=fs.readFileSync('LitleHelperServer/wwwroot/messenger/'+page+'.js','utf8');
  const signature=page==='call'?'async function acquireLocalMedia()':'async function safeGetUserMedia(isVideo)';
  const start=source.indexOf(signature);
  const end=source.indexOf(page==='call'?'// 5. STUN/TURN':'async function startCall',start);
  vm.runInContext(source.slice(start,end),c);
  const stream=await vm.runInContext(page==='call'?'acquireLocalMedia()':'safeGetUserMedia(true)',c);
  assert.equal(stream.tracks.includes(video),true);
  assert.equal(stream.tracks.includes(silent),true);
  assert.equal(notices.some(s=>s.includes('Микрофон')&&s.includes('доступ запрещён')),true);
  assert.equal(notices.some(s=>s.includes('Камера')),false);
 });
}

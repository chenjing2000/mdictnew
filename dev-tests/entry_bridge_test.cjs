const fs = require('fs');
const vm = require('vm');
const assert = require('assert');
const script = fs.readFileSync(__dirname + '/../Mdict.Wpf/WebView/EntryBridge.js', 'utf8');
let click;
let handlers = {}, messages = [], selection = '';
let navigations = [];
let location = {protocol:'https:',hostname:'mdict.invalid',assign:url=>navigations.push(url)};
const env={window:{getSelection:()=>({toString:()=>selection}),chrome:{webview:{postMessage:m=>messages.push(m)}},addEventListener:(event,fn,capture)=>{assert.equal(capture,true);handlers[event]=fn;if(event==='click')click=fn;}},location,console:{warn:()=>{}}};
vm.runInNewContext(script,env);
function fire(href){
 let calls=[];
 let anchor={getAttribute:()=>href};
 let element={nodeType:1,closest:(sel)=>{assert.equal(sel,'a[href]');return anchor;}};
 let e={target:element,preventDefault:()=>calls.push('prevent'),stopImmediatePropagation:()=>calls.push('immediate'),stopPropagation:()=>calls.push('stop')};
 const before=navigations.length;
 click(e);
 return {calls,navigation:navigations.length>before?navigations[navigations.length-1]:null};
}
function verify(href,key){ const result=fire(href);assert.deepEqual(result.calls,['prevent','immediate','stop']);assert.equal(result.navigation,'https://mdict.invalid/lookup?key='+encodeURIComponent(key)); console.log('PASS '+href); }
verify('entry://cooking-apple','cooking-apple');
verify('entry://appleCC','appleCC');
verify('entry://@ox3000&level=a1','@ox3000&level=a1');
verify('entry://a%2Bb','a+b');
verify('entry://the%20big%20apple','the big apple');
verify('entry://a%2520b','a%20b');
for(const url of ['#apple_idmgs_1','sound://apple__gb_5.mp3','https://example.com/']){let r=fire(url);assert.equal(r.navigation,null);assert.equal(r.calls.length,0);console.log('PASS unhandled '+url);}
for(const url of ['entry://','entry://abc%zz','entry://x%0Ay']){let r=fire(url);assert.equal(r.navigation,null);console.log('PASS rejected '+url);}
function gesture(name,x,y,interactive=false){
 handlers[name]({button:0,clientX:x,clientY:y,target:{nodeType:1,closest:()=>interactive?{}:null}});
}
selection='apple';
gesture('mousedown',10,10);gesture('mouseup',10,10);
assert.equal(messages.length,0);console.log('PASS ordinary click leaves old selection alone');
selection='  selected text\n中文  ';
gesture('mousedown',10,10);gesture('mouseup',40,10);
assert.equal(messages.pop().type,'mdict-copy');console.log('PASS drag copies without lookup');
selection='banana';
gesture('mousedown',10,10);gesture('mouseup',10,10);gesture('dblclick',10,10);
assert.equal(messages.length,1);assert.equal(messages.pop().type,'mdict-copy-lookup');console.log('PASS double click sends one copy and lookup');
gesture('mousedown',10,10,true);gesture('mouseup',40,10,true);gesture('dblclick',10,10,true);
assert.equal(messages.length,0);console.log('PASS interactive controls keep their behavior');
selection=' ';
gesture('mousedown',10,10);gesture('mouseup',40,10);gesture('dblclick',10,10);
assert.equal(messages.length,0);console.log('PASS empty selection ignored');
selection='bad\nkey';gesture('dblclick',10,10);
assert.equal(messages.length,0);console.log('PASS invalid lookup selection rejected');
console.log('EntryBridge all tests passed');

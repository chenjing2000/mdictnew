const fs = require('fs');
const vm = require('vm');
const assert = require('assert');
const script = fs.readFileSync(__dirname + '/../Mdict.Wpf/WebView/EntryBridge.js', 'utf8');
let click;
let navigations = [];
let location = {protocol:'https:',hostname:'mdict.invalid',assign:url=>navigations.push(url)};
const env={window:{addEventListener:(event,fn,capture)=>{assert.equal(event,'click');assert.equal(capture,true);click=fn;}},location,console:{warn:()=>{}}};
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
console.log('EntryBridge all tests passed');

// Synthetic local video + wait-only actions. Never records the desktop or sends input.
import {spawn} from 'node:child_process';
import readline from 'node:readline';
import path from 'node:path';
import assert from 'node:assert/strict';
const [exe,video]=process.argv.slice(2);
assert(exe&&video,'Usage: node test-packaged-engine.mjs ENGINE_EXE SYNTHETIC_MP4');
const child=spawn(path.resolve(exe),['--bridge','--parent-pid',String(process.pid)],{windowsHide:true,stdio:['pipe','pipe','pipe']});
let state,id=0;
const pending=new Map();
const delay=ms=>new Promise(r=>setTimeout(r,ms));
readline.createInterface({input:child.stdout}).on('line',line=>{
 try{const msg=JSON.parse(line);if(msg.type==='state')state=msg.state;
 else if(pending.has(msg.id)){const p=pending.get(msg.id);pending.delete(msg.id);msg.ok?p.resolve():p.reject(Error(msg.error));}}catch{}
});
const command=(action,data={})=>new Promise((resolve,reject)=>{
 const next=++id;pending.set(next,{resolve,reject});child.stdin.write(JSON.stringify({id:next,action,...data})+'\n');
});
const wait=async fn=>{const end=Date.now()+20000;while(!fn()){if(Date.now()>end)throw Error('Engine state timeout');await delay(100);}};
const timeout=setTimeout(()=>{child.kill();console.error('ENGINE_TEST_TIMEOUT');process.exitCode=1;},45000);
try{
 await wait(()=>state?.phase==='idle');
 await command('open',{path:path.resolve(video)});
 await assert.rejects(command('add',{kind:'Wait',at:0,seconds:0.01}));
 await command('add',{kind:'Wait',at:0,seconds:0.1});
 await wait(()=>state.events.length===1);
 await command('edit',{index:0,at:0,seconds:0.2});
 await command('save');
 await command('open',{path:path.resolve(video)});
 await wait(()=>state.events[0]?.text==='0.2');
 await command('run',{repeats:2});
 await wait(()=>state.busy);
 await wait(()=>!state.busy);
 assert.equal(state.iteration,2);
 assert.equal(state.events[0].result,'실행 완료');
 console.log(JSON.stringify({ok:true,customWait:state.events[0].text,repeats:state.iteration,result:state.events[0].result}));
}catch(error){console.error(error.message);process.exitCode=1;}
finally{
 const ended=new Promise(resolve=>child.once('exit',resolve));
 child.stdin.end();
 await Promise.race([ended,delay(5000).then(()=>child.kill())]);
 clearTimeout(timeout);
}

const {spawn}=require('node:child_process');
const readline=require('node:readline');
const path=require('node:path');
module.exports=function nativeBridge(onState,options={}){
 const executable=options.packaged ? path.join(process.resourcesPath,'engine/공공AX-업무매크로.exe') : path.join(__dirname,'../artifacts/bridge/공공AX-업무매크로.exe');
 const child=spawn(executable,['--bridge','--parent-pid',String(process.pid)],{windowsHide:true,stdio:['pipe','pipe','pipe']});
 let next=0,dead=false;const pending=new Map();
 const fail=error=>{dead=true;for(const p of pending.values()){clearTimeout(p.timer);p.reject(error);}pending.clear();onState({phase:'error',busy:false,message:error.message,events:[]});};
 child.on('error',fail);child.on('exit',()=>fail(Error('기록 엔진이 종료되었습니다. 앱을 다시 실행하세요.')));
 child.stderr.on('data',data=>console.error(String(data)));
 readline.createInterface({input:child.stdout}).on('line',line=>{try{const msg=JSON.parse(line);if(msg.type==='state')onState(msg.state);else if(pending.has(msg.id)){const p=pending.get(msg.id);pending.delete(msg.id);clearTimeout(p.timer);msg.ok?p.resolve():p.reject(Error(msg.error));}}catch{/* Ignore native diagnostics. */}});
 return {command(action,data={}){if(dead)return Promise.reject(Error('기록 엔진에 연결할 수 없습니다.'));return new Promise((resolve,reject)=>{const id=++next;const timer=setTimeout(()=>{pending.delete(id);reject(Error('엔진 응답 지연: 상태를 확인하세요.'));},15000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({...data,id,action})+'\n');});},close(){child.stdin.end();},child};
};

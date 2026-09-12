import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {rcedit} from 'rcedit';
const studio=path.dirname(fileURLToPath(import.meta.url)), repo=path.dirname(studio);
// Avoid the native cpSync fast path, which can crash on non-ASCII Windows paths.
function copyTree(source,target){
 fs.mkdirSync(target,{recursive:true});
 for(const item of fs.readdirSync(source,{withFileTypes:true})){
  const from=path.join(source,item.name),to=path.join(target,item.name);
  if(item.isDirectory())copyTree(from,to);
  else if(item.isFile())fs.copyFileSync(from,to);
  else throw Error('Unexpected symlink/special file: '+from);
 }
}
const out=path.resolve(process.argv[2]||'');
const allowed=path.join(repo,'artifacts')+path.sep;
assert(out.startsWith(allowed)&&out!==path.join(repo,'artifacts'),'Output must be an artifacts subdirectory');
assert(!fs.existsSync(out),'Refusing to overwrite existing package');
const pkg=JSON.parse(fs.readFileSync(path.join(studio,'package.json'),'utf8'));
const lock=JSON.parse(fs.readFileSync(path.join(studio,'package-lock.json'),'utf8'));
const native=path.join(repo,'artifacts/publish/win-x64');
assert(fs.existsSync(path.join(native,'공공AX-업무매크로.exe')));
copyTree(path.join(studio,'node_modules/electron/dist'),out);
const executable=path.join(out,'공공AX-업무매크로.exe');
fs.renameSync(path.join(out,'electron.exe'),executable);
const defaultApp=path.join(out,'resources/default_app.asar');
if(fs.existsSync(defaultApp))fs.unlinkSync(defaultApp);
const app=path.join(out,'resources/app');
fs.mkdirSync(app,{recursive:true});
for(const name of ['electron.cjs','preload.cjs','native-bridge.cjs','window-layout.cjs','package-smoke.cjs'])
 fs.copyFileSync(path.join(studio,name),path.join(app,name));
fs.writeFileSync(path.join(app,'package.json'),JSON.stringify({name:'gonggong-ax-series4',productName:'공공 AX 업무 매크로',version:pkg.version,main:'electron.cjs'},null,2));
copyTree(path.join(studio,'dist'),path.join(app,'dist'));
copyTree(native,path.join(out,'resources/engine'));
for(const name of ['LICENSE','THIRD_PARTY_NOTICES.md','OPEN_SOURCE_COMPONENTS.md'])
 fs.copyFileSync(path.join(repo,name),path.join(out,name==='LICENSE'?'LICENSE.txt':name));
fs.copyFileSync(path.join(repo,'docs/ELECTRON-README-FIRST.txt'),path.join(out,'README-FIRST.txt'));
copyTree(path.join(repo,'docs/assets/series4-comic/intro-20260912'),path.join(out,'소개 만화'));
copyTree(path.join(native,'licenses'),path.join(out,'licenses'));
const npmNotices=path.join(out,'licenses/npm');
fs.mkdirSync(npmNotices,{recursive:true});
const frontend=[];
for(const [key,value] of Object.entries(lock.packages)){
 if(!key||value.dev)continue;
 const name=key.replace(/^node_modules\//,'');
 const dir=path.join(studio,key);
 const files=fs.readdirSync(dir).filter(n=>/^(license|copying|notice)/i.test(n)&&fs.statSync(path.join(dir,n)).isFile());
 assert(files.length,'Missing licence for '+name);
 const target=path.join(npmNotices,name.replaceAll('/','__'));
 fs.mkdirSync(target,{recursive:true});
 for(const file of files)fs.copyFileSync(path.join(dir,file),path.join(target,file));
 frontend.push({name,version:value.version,license:value.license||'NOASSERTION'});
}
fs.writeFileSync(path.join(out,'licenses/npm-components.json'),JSON.stringify(frontend,null,2));
const sbom=JSON.parse(fs.readFileSync(path.join(native,'SBOM.spdx.json'),'utf8'));
const deps=[...frontend,{name:'electron',version:lock.packages['node_modules/electron'].version,license:'MIT'}];
for(const [i,p] of deps.entries()){
 const id='SPDXRef-Frontend-'+i;
 sbom.packages.push({name:p.name,SPDXID:id,versionInfo:p.version,downloadLocation:'https://www.npmjs.com/package/'+p.name,
  filesAnalyzed:false,licenseDeclared:p.license,licenseConcluded:'NOASSERTION',copyrightText:'NOASSERTION'});
 sbom.relationships.push({spdxElementId:'SPDXRef-Package-GonggongAX-Series4',relationshipType:'DEPENDS_ON',relatedSpdxElement:id});
}
sbom.creationInfo.creators.push('Tool: studio/package-electron.mjs');
fs.writeFileSync(path.join(out,'SBOM.spdx.json'),JSON.stringify(sbom,null,2));
await rcedit(executable,{'file-version':pkg.version,'product-version':pkg.version,'version-string':{
 ProductName:'공공 AX 업무 매크로',FileDescription:'공공 AX 로컬 시리즈 4 · Electron 스튜디오',
 CompanyName:'Public AX Local contributors',OriginalFilename:'공공AX-업무매크로.exe'}});
for(const file of ['resources/app/dist/index.html','resources/engine/coreclr.dll','resources/engine/ScreenRecorderLib.dll','resources/engine/uiohook.dll','LICENSES.chromium.html','소개 만화/01-record.png','소개 만화/02-repeat-wait.png'])
 assert(fs.existsSync(path.join(out,file)),file);
console.log('Electron + native self-contained package ready: '+out);

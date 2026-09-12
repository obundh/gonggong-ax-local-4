import {useId,useRef,useState} from 'react';
import {Plus} from 'lucide-react';
import {useProject} from './Project';

export function AddAction(){
 const p=useProject(),id=useId(),trigger=useRef<HTMLButtonElement>(null);
 const [open,setOpen]=useState(false),[kind,setKind]=useState('Wait'),[at,setAt]=useState('0'),[seconds,setSeconds]=useState('2'),[text,setText]=useState(''),[x,setX]=useState('0'),[y,setY]=useState('0');
 const close=()=>{setOpen(false);trigger.current?.focus();};
 return <div className="action-add">
  <div className="action-add-heading"><span>행동 추가</span><button ref={trigger} type="button" className="action-add-icon" aria-label="행동 추가" title="행동 추가" aria-expanded={open} aria-controls={id} disabled={p.busy||!p.connected||!p.source} onClick={()=>{if(open)close();else{setAt(p.cursor.toFixed(2));setOpen(true);}}}><Plus size={16}/></button></div>
  {open&&<form id={id} className="action-add-form" onKeyDown={e=>{if(e.key==='Escape'){e.stopPropagation();close();}}} onSubmit={async e=>{e.preventDefault();const ok=await p.command('add',{kind,at:Number(at),seconds:Number(seconds),text,x:Number(x),y:Number(y)});if(ok)close();}}>
   <label>동작<select autoFocus value={kind} disabled={p.busy} onChange={e=>setKind(e.target.value)}><option value="Wait">대기</option><option value="TextEntry">텍스트 입력</option><option value="MouseLeftClick">왼쪽 클릭</option><option value="MouseRightClick">오른쪽 클릭</option></select></label>
   <label>영상 시점(초)<input required type="number" min="0" max="86400" step=".01" value={at} onChange={e=>setAt(e.target.value)}/></label>
   {kind==='Wait'&&<label>대기(초)<input required type="number" min=".1" max="3600" step=".1" value={seconds} onChange={e=>setSeconds(e.target.value)}/></label>}
   {kind==='TextEntry'&&<label className="action-wide">입력 내용<textarea required maxLength={10000} rows={2} value={text} onChange={e=>setText(e.target.value)}/></label>}
   {kind.startsWith('Mouse')&&<><label>X<input required type="number" value={x} onChange={e=>setX(e.target.value)}/></label><label>Y<input required type="number" value={y} onChange={e=>setY(e.target.value)}/></label></>}
   <p className="action-wide">{kind==='Wait'?'실행만 대기 · 영상 시점 유지':'같은 시점의 기존 행동 다음에 추가'}</p>
   <div className="action-wide action-add-actions"><button type="button" onClick={close}>취소</button><button type="submit" disabled={p.busy}>추가</button></div>
  </form>}
 </div>;
}

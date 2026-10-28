import {BabylonAdapter} from './renderer.mjs';
import {navigation} from './navigation.mjs';
import {SectionGrips} from './section-grips.mjs';
const $=id=>document.getElementById(id),bridge=window.chrome.webview;
let renderer=navigation(new BabylonAdapter()),engineName='babylon',pending,sceneBytes,loaded=false,lastBounds=0,switching=false,awaitingBoundsEcho=false,identityByMesh=new Map(),identityByElement=new Map();
let selected=null;const grips=new SectionGrips(()=>renderer);
const send=m=>bridge.postMessage(m),fail=e=>{const message=String(e?.message||e);if(!loaded)$('hint').textContent='3D model could not be shown: '+message;send({type:'error',message});};
const dimensions=()=>{};
let defaults={square:3.048,above:3.048,below:.1524,unit:'feet'},draft={...defaults};
function showDefaults(){
 const factor=draft.unit==='feet'?.3048:1;
 for(const key of ['square','above','below']){const input=$('default-'+key);input.value=(draft[key]/factor).toFixed(draft.unit==='feet'?2:4);input.min=key==='below'?'0':String(.01/factor);}
 for(const unit of ['feet','meters'])$('unit-'+unit).setAttribute('aria-pressed',String(draft.unit===unit));
 document.querySelectorAll('.box-unit').forEach(e=>e.textContent=draft.unit==='feet'?'ft':'m');
}
for(const unit of ['feet','meters'])$('unit-'+unit).onclick=()=>{draft.unit=unit;showDefaults();};
for(const key of ['square','above','below'])$('default-'+key).oninput=()=>{draft[key]=Number($('default-'+key).value)*(draft.unit==='feet'?.3048:1);};
$('save-box-defaults').onclick=()=>{
 const valid=['square','above','below'].every(key=>$('default-'+key).value!==''&&Number.isFinite(draft[key])&&draft[key]>=(key==='below'?0:.01)&&draft[key]<=10000000);
 $('defaults-status').classList.toggle('error',!valid);
 if(!valid){$('defaults-status').textContent='Enter positive sizes; below level can be zero.';return;}
 $('defaults-status').textContent='Saving…';send({type:'setBoxDefaults',...draft});
};
showDefaults();

function options(){const result={};for(const name of ['quality','style'])result[name]=$(name).value;for(const name of ['shadows','occlusion','caps','edges'])result[name]=$(name).checked;for(const name of ['exposure','environment'])result[name]=Number($(name).value);return result;}
function connect(){renderer.orbitSelection=selected;renderer.onViewChanged=state=>{updateCube(state);grips.update();};renderer.onBounds=(b,final)=>{pending=b;dimensions(b);const now=performance.now();if(final||now-lastBounds>40){lastBounds=now;if(final)awaitingBoundsEcho=true;send({...b,final});}};}
connect();
async function create(name){return navigation(name==='three'?new(await import('./vendor/three-adapter.mjs')).ThreeAdapter():new BabylonAdapter());}
async function switchEngine(name){if(switching||name===engineName)return;switching=true;$('engine').disabled=true;const previous=engineName,camera=renderer.getCameraState?.(),box=renderer.pending||pending;renderer.dispose();loaded=false;try{renderer=await create(name);engineName=name;connect();renderer.configure(options());if(sceneBytes){await renderer.load(sceneBytes);loaded=true;if(box)renderer.box(box,true);if(camera)renderer.setCameraState?.(camera);}pending=box;$('hint').textContent=renderer.name?renderer.name+' · same model and section bounds':'Babylon.js · same model and section bounds';}catch(e){console.error('ENGINE START FAILED',name,e.stack||e.message||String(e));renderer.dispose();engineName=previous;renderer=await create(previous);connect();renderer.configure(options());if(sceneBytes){await renderer.load(sceneBytes);loaded=true;if(box)renderer.box(box,true);}pending=box;$('engine').value=previous;$('hint').textContent='Renderer could not start; previous renderer restored. '+e.message;}finally{$('occlusion').disabled=false;$('engine').disabled=false;switching=false;}}
function closeMenus(){for(const name of ['settings','tools']){ $(name+'-panel').hidden=true;$(name+'-toggle').setAttribute('aria-expanded','false');}}
for(const name of ['settings','tools'])$(name+'-toggle').onclick=()=>{const open=$(name+'-panel').hidden;closeMenus();$(name+'-panel').hidden=!open;$(name+'-toggle').setAttribute('aria-expanded',String(open));};
document.addEventListener('keydown',e=>{if(e.key==='Escape')closeMenus();});
$('engine').onchange=()=>switchEngine($('engine').value);
// The viewer hands the GLB over in shared memory (or in chunks on an old
// WebView2 runtime); there is no network request for the model.
let incoming=null,incomingTimer=0,earlyBuffer=null;
async function deliver(bytes){
 if(!bytes||!incoming||sceneBytes&&loaded)return;
 if(bytes.byteLength!==incoming.size){earlyBuffer=null;throw new Error('Incomplete 3D model transfer ('+bytes.byteLength+' of '+incoming.size+' bytes)');}
 clearTimeout(incomingTimer);earlyBuffer=null;sceneBytes=bytes;incoming=null;
 const started=performance.now();await renderer.load(sceneBytes);loaded=true;$('engine').disabled=false;if(pending)renderer.box(pending,true);
 send({type:'loaded',ms:Math.round(performance.now()-started)});
}
bridge.addEventListener('sharedbufferreceived',async e=>{const shared=e.getBuffer();let copy=null;try{if(e.additionalData?.type==='sceneBuffer')copy=new Uint8Array(shared.slice(0));}finally{bridge.releaseBuffer?.(shared);}
 if(!copy)return;try{if(incoming)await deliver(copy);else earlyBuffer=copy;}catch(error){$('engine').disabled=false;fail(error);}});
bridge.addEventListener('message',async({data:m})=>{try{
 if(m.type==='scene'){identityByMesh=new Map((m.identities||[]).map(x=>[x.meshName,x]));identityByElement=new Map((m.identities||[]).map(x=>[x.meshName.split('/')[0],x]));$('engine').disabled=true;$('hint').textContent='Loading 3D model ('+(m.size/1048576).toFixed(1)+' MB)…';incoming={size:m.size,chunks:null,received:0};clearTimeout(incomingTimer);incomingTimer=setTimeout(()=>{if(incoming&&!sceneBytes)fail(new Error('The 3D model was not received from the viewer. Close and reopen 3D.'));},60000);await deliver(earlyBuffer);}
 if(m.type==='sceneChunk'){if(!incoming||m.total!==incoming.size||sceneBytes)return;incoming.chunks??=new Uint8Array(m.total);const part=Uint8Array.from(atob(m.data),c=>c.charCodeAt(0));incoming.chunks.set(part,m.offset);incoming.received+=part.length;$('hint').textContent='Loading 3D model… '+Math.round(100*incoming.received/m.total)+'%';if(incoming.received===m.total)await deliver(incoming.chunks);}
 if(m.type==='box'){pending=m;dimensions(m);if(loaded){renderer.box(m,false,awaitingBoundsEcho&&!m.fitBox);if(m.fitBox){cancelAnimationFrame(renderer.animation);const state=renderer.getCameraState();state.target=renderer.bounds.min.map((v,k)=>(v+renderer.bounds.max[k])/2);state.radius=Math.max(m.width,m.depth,m.above+m.below)*(.7/.45);renderer.setCameraState(state);}}awaitingBoundsEcho=false;}
 if(m.type==='boxDefaults'){defaults={square:m.square,above:m.above,below:m.below,unit:m.unit==='meters'?'meters':'feet'};draft={...defaults};showDefaults();$('defaults-status').classList.remove('error');$('defaults-status').textContent='Saved defaults · used when opening 3D and by Reset box.';}
 if(m.type==='settingsError'){$('defaults-status').classList.add('error');$('defaults-status').textContent='Could not save: '+m.message;}
 if(m.type==='docked')$('undock').textContent=m.value?'Undock':'Dock';
 }catch(e){$('engine').disabled=false;fail(e);}});
for(const key of ['quality','style','shadows','occlusion','caps','edges','exposure','environment'])$(key).onchange=()=>renderer.configure(options());
$('reset').onclick=()=>renderer.home();$('box-reset').onclick=()=>send({type:'resetBounds'});$('hide').onclick=()=>send({type:'hide'});$('undock').onclick=()=>send({type:'undock'});
for(const type of ['pointerdown','pointermove','pointerup','wheel','keydown'])document.body.addEventListener(type,e=>{if(e.target.id==='scene')renderer.wake();});
let measureStart=null,measuring=false,downPoint=null,lastHover=0;
function properties(hit){const mesh=hit?.pickedMesh;if(!mesh)return;selected=mesh.name;renderer.select?.(selected);const id=identityByMesh.get(mesh.name)||identityByElement.get(mesh.name.split('/')[0])||mesh.metadata?.revit||{};const values=[['Element ID',id.elementId],['Unique ID',id.uniqueId],['Category',id.category],['Family',id.familyName],['Type',id.typeName],['Level',id.levelName],['Mark',id.mark],['Material',mesh.metadata?.original?.name||mesh.userData?.original?.name||id.materialName],['Link',id.linkInstanceId],...Object.entries(id.parameters||{})];const dl=document.createElement('dl');for(const [name,value]of values){if(!value)continue;const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=name;dd.textContent=String(value);dl.append(dt,dd);}$('property-values').replaceChildren(dl);$('properties').hidden=false;renderer.wake();}
document.body.addEventListener('pointerdown',e=>{if(e.target.id==='scene'){closeMenus();downPoint=[e.clientX,e.clientY];}});
document.body.addEventListener('pointerup',e=>{if(e.target.id!=='scene'||e.button!==0||!downPoint||Math.hypot(e.clientX-downPoint[0],e.clientY-downPoint[1])>4)return;const hit=renderer.pick(e);if(!hit){if(!measuring)clearSelection();return;}properties(hit);if(measuring&&hit.pickedPoint){const p=hit.pickedPoint.asArray?hit.pickedPoint.asArray():[hit.pickedPoint.x,hit.pickedPoint.y,hit.pickedPoint.z];if(measureStart){$('hint').textContent='Distance: '+(Math.hypot(...p.map((v,k)=>v-measureStart[k]))*100).toFixed(2)+' cm';measureStart=null;measuring=false;$('measure').textContent='Measure';}else{measureStart=p;$('hint').textContent='Pick the second point.';}}});
document.body.addEventListener('pointermove',e=>{if(e.target.id==='scene'&&e.buttons===0&&performance.now()-lastHover>150){lastHover=performance.now();const hit=renderer.pick(e);const id=hit&&(identityByMesh.get(hit.pickedMesh.name)||identityByElement.get(hit.pickedMesh.name.split('/')[0]));$('scene').title=id?[id.category,id.familyName,id.typeName,id.mark].filter(Boolean).join(' · '):'';}});
document.body.addEventListener('dblclick',e=>{if(e.target.id==='scene'){const hit=renderer.pick(e);if(hit){properties(hit);renderer.focus?.(selected,hit);}}});
function clearSelection(){selected=null;renderer.select?.(null);renderer.orbitSelection=null;$('properties').hidden=true;}
$('clear-selection').onclick=clearSelection;$('close-properties').onclick=()=>{$('properties').hidden=true;};
$('focus-selection').onclick=()=>selected&&renderer.focus?.(selected);
$('hide-selection').onclick=()=>selected&&renderer.visibility?.('hide',selected);
$('isolate-selection').onclick=()=>selected&&renderer.visibility?.('isolate',selected);
$('xray-selection').onclick=()=>selected&&renderer.visibility?.('xray',selected);
$('show-all').onclick=()=>renderer.visibility?.('all',null);
$('measure').onclick=()=>{closeMenus();measuring=!measuring;measureStart=null;$('measure').textContent=measuring?'Cancel measure':'Measure';$('hint').textContent=measuring?'Pick the first point.':'Measurement cancelled.';};
window.addEventListener('resize',()=>renderer.wake());window.addEventListener('pagehide',()=>renderer.dispose());window.addEventListener('unhandledrejection',e=>fail(e.reason));
const views={front:[Math.PI/2,Math.PI/2],back:[-Math.PI/2,Math.PI/2],right:[0,Math.PI/2],left:[Math.PI,Math.PI/2],top:[-Math.PI/2,.000001],bottom:[-Math.PI/2,Math.PI-.000001]};
let cubeKey='';
function updateCube(state){if(!state)return;const key=[state.alpha,state.beta].map(v=>v.toFixed(5)).join();if(key===cubeKey)return;cubeKey=key;document.querySelector('.cube-body').style.transform=`rotateX(${(state.beta-Math.PI/2)*180/Math.PI}deg) rotateY(${(state.alpha-Math.PI/2)*180/Math.PI}deg)`;}
function standardView(name){const state=renderer.getCameraState?.(),preset=views[name];if(!state||!preset)return;state.alpha=preset[0];state.beta=preset[1];renderer.setCameraState(state);updateCube(state);}
const cube=document.querySelector('.view-cube');let cubeDrag,suppressCubeClick=false;
cube.addEventListener('pointerdown',e=>{const state=renderer.getCameraState?.();if(!state)return;cubeDrag={x:e.clientX,y:e.clientY,state,moved:false,face:e.target.closest('[data-view]')?.dataset.view};cube.setPointerCapture(e.pointerId);});
cube.addEventListener('pointermove',e=>{if(!cubeDrag)return;const d=cubeDrag,dx=e.clientX-d.x,dy=e.clientY-d.y;if(Math.hypot(dx,dy)>4)d.moved=true;if(!d.moved)return;const state={...d.state,alpha:d.state.alpha-dx*.012,beta:Math.max(.000001,Math.min(Math.PI-.000001,d.state.beta-dy*.012))};renderer.setCameraState(state);updateCube(state);});
cube.addEventListener('pointerup',e=>{suppressCubeClick=!!cubeDrag?.moved;if(cubeDrag&&!cubeDrag.moved&&cubeDrag.face)standardView(cubeDrag.face);cubeDrag=null;if(cube.hasPointerCapture(e.pointerId))cube.releasePointerCapture(e.pointerId);setTimeout(()=>suppressCubeClick=false,0);});
cube.addEventListener('keydown',e=>{if(e.key==='PageUp'||e.key==='PageDown'){standardView(e.key==='PageUp'?'top':'bottom');e.preventDefault();return;}if(e.key==='Home'){renderer.reset();e.preventDefault();return;}const delta={ArrowLeft:[-.25,0],ArrowRight:[.25,0],ArrowUp:[0,-.25],ArrowDown:[0,.25]}[e.key],state=renderer.getCameraState?.();if(delta&&state){state.alpha+=delta[0];state.beta=Math.max(.000001,Math.min(Math.PI-.000001,state.beta+delta[1]));renderer.setCameraState(state);updateCube(state);e.preventDefault();}});
cube.addEventListener('click',e=>{const face=e.target.closest('[data-view]');if(face&&!suppressCubeClick&&e.detail===0)standardView(face.dataset.view);});
export const testing={get renderer(){return renderer;},switchEngine};send({type:'ready'});

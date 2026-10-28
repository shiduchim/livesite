import {BabylonAdapter} from './renderer.mjs';
import {navigation} from './navigation.mjs';
import {SectionGrips} from './section-grips.mjs';
import {MeasureTool} from './measure.mjs';
const $=id=>document.getElementById(id),bridge=window.chrome.webview;
let renderer=navigation(new BabylonAdapter()),engineName='babylon',pending,sceneBytes,loaded=false,lastBounds=0,switching=false,awaitingBoundsEcho=false,identityByMesh=new Map(),identityByElement=new Map();
let selected=null;const grips=new SectionGrips(()=>renderer);
let unit='feet';const measure=new MeasureTool(()=>renderer,()=>unit);
const send=m=>bridge.postMessage(m),fail=e=>{const message=String(e?.message||e);if(!loaded)loading('3D model could not be shown: '+message,true);send({type:'error',message});};

// Loading overlay: a spinner (no engine logo) until the model is drawn.
function loading(text,error=false){const box=$('loading');if(text===null){box.hidden=true;return;}box.hidden=false;box.classList.toggle('error',error);$('loading-text').textContent=text;}

// View settings persist per user (WebView2 keeps local storage in the viewer's profile).
const storeKey='offline3d.view.v1',checks=['edges','occlusion','caps','shadows','navigator'];
function saved(){try{return JSON.parse(localStorage.getItem(storeKey)||'{}')||{};}catch{return{};}}
function save(){try{localStorage.setItem(storeKey,JSON.stringify({engine:engineName,quality:$('quality').value,style:$('style').value,brightness:Number($('brightness').value),...Object.fromEntries(checks.map(k=>[k,$(k).checked]))}));}catch{}}
{const s=saved();for(const k of ['quality','style'])if(s[k]&&$(k).querySelector(`option[value="${s[k]}"]`))$(k).value=s[k];for(const k of checks)if(typeof s[k]==='boolean')$(k).checked=s[k];if(Number.isFinite(s.brightness))$('brightness').value=s.brightness;}

// Three.js has no usable Realistic style: the option stays visible but greyed out.
function syncStyles(){const realistic=$('style').querySelector('option[value=realistic]');realistic.disabled=engineName==='three';realistic.textContent=engineName==='three'?'Realistic (Babylon.js only)':'Realistic';if(realistic.disabled&&$('style').value==='realistic')$('style').value='shaded';}
function syncNavigator(){document.body.classList.toggle('no-navigator',!$('navigator').checked);}
syncNavigator();

// Section box settings, as in the ClickTo3D Revit add-in: the size every new 3D view
// starts with. Changes are saved at once (and applied to the open box); a box
// stretched in 3D is not saved. Values are kept in metres and shown in feet or metres.
const keys=['above','below','square'],showDefault='';
let boxSettings=null;
const factor=()=>unit==='feet'?.3048:1,shown=v=>(v/factor()).toFixed(2);
function showBoxSettings(){for(const u of ['feet','meters'])$('unit-'+u).setAttribute('aria-pressed',String(unit===u));document.querySelectorAll('.box-unit').forEach(e=>e.textContent=unit==='feet'?'ft':'m');if(!boxSettings)return;for(const k of keys){const input=$('box-'+k);if(document.activeElement!==input||!input.dataset.dirty)input.value=shown(boxSettings[k]);input.classList.remove('invalid');delete input.dataset.dirty;}$('restore-box-default').disabled=!!boxSettings.isDefault;}
function status(text,error=false){$('defaults-status').textContent=text;$('defaults-status').classList.toggle('error',error);}
// Commits one field. Height below may be 0; the others must be above 0.
function commit(key){const input=$('box-'+key);if(!boxSettings)return;const value=Number(input.value.trim().replace(',','.'));delete input.dataset.dirty;
 if(input.value.trim()===''||!Number.isFinite(value)||(key==='below'?value<0:value<=0)||value>1000000){input.classList.add('invalid');status(key==='below'?'Height below level must be 0 or more.':'Height above level and square size must be greater than 0.',true);return;}
 input.classList.remove('invalid');status(showDefault);if(value.toFixed(2)===shown(boxSettings[key]))return;
 boxSettings={...boxSettings,[key]:value*factor()};input.value=value.toFixed(2);send({type:'setBoxDefaults',square:boxSettings.square,above:boxSettings.above,below:boxSettings.below});}
// ▲▼ and the arrow keys add or subtract exactly 1 in the current unit and never step to an invalid value.
function step(key,direction){const input=$('box-'+key),value=Number(input.value.trim().replace(',','.'));if(!Number.isFinite(value))return;let next=Math.round((value+direction)*100)/100;if(key==='below'){if(next<0){if(value<=0)return;next=0;}}else if(next<=0)return;input.value=next.toFixed(2);commit(key);}
for(const key of keys){const input=$('box-'+key);input.oninput=()=>{input.dataset.dirty='1';};input.onchange=()=>commit(key);input.onfocus=()=>input.select();input.onkeydown=e=>{if(e.key==='Enter'){commit(key);input.select();e.preventDefault();}else if(e.key==='ArrowUp'||e.key==='ArrowDown'){step(key,e.key==='ArrowUp'?1:-1);e.preventDefault();}else if(e.key==='Escape'){delete input.dataset.dirty;showBoxSettings();status(showDefault);}};
 for(const button of input.parentElement.querySelectorAll('[data-step]'))button.onclick=()=>step(key,Number(button.dataset.step));}
for(const u of ['feet','meters'])$('unit-'+u).onclick=()=>{if(unit===u)return;unit=u;showBoxSettings();measure.draw();send({type:'setUnit',unit:u});};
$('restore-box-default').onclick=()=>{status(showDefault);send({type:'restoreBoxDefault'});};

function options(){const result={};for(const name of ['quality','style'])result[name]=$(name).value;for(const name of ['shadows','occlusion','caps','edges'])result[name]=$(name).checked;result.brightness=Number($('brightness').value);return result;}
function connect(){renderer.orbitSelection=selected;renderer.onViewChanged=state=>{updateCube(state);grips.update();measure.draw();};renderer.onBounds=(b,final)=>{pending=b;const now=performance.now();if(final||now-lastBounds>40){lastBounds=now;if(final)awaitingBoundsEcho=true;send({...b,final});}};}
connect();renderer.configure(options());
async function create(name){return navigation(name==='three'?new(await import('./vendor/three-adapter.mjs')).ThreeAdapter():new BabylonAdapter());}
async function switchEngine(name){if(switching||name===engineName)return;switching=true;$('engine').disabled=true;const previous=engineName,camera=renderer.getCameraState?.(),box=renderer.pending||pending;measure.stop();renderer.dispose();loaded=false;engineName=name;syncStyles();if(sceneBytes)loading('Starting '+(name==='three'?'Three.js':'Babylon.js')+'…');try{renderer=await create(name);connect();renderer.configure(options());if(sceneBytes){await renderer.load(sceneBytes);loaded=true;if(box)renderer.box(box,true);if(camera)renderer.setCameraState?.(camera);}pending=box;loading(null);}catch(e){console.error('ENGINE START FAILED',name,e.stack||e.message||String(e));renderer.dispose();engineName=previous;syncStyles();renderer=await create(previous);connect();renderer.configure(options());if(sceneBytes){await renderer.load(sceneBytes);loaded=true;if(box)renderer.box(box,true);}pending=box;loading(null);$('hint').textContent='Renderer could not start; previous renderer restored. '+e.message;}finally{$('engine').value=engineName;$('engine').disabled=false;switching=false;save();}}
function closeMenus(){for(const name of ['settings','tools']){$(name+'-panel').hidden=true;$(name+'-toggle').setAttribute('aria-expanded','false');}}
for(const name of ['settings','tools'])$(name+'-toggle').onclick=()=>{const open=$(name+'-panel').hidden;closeMenus();$(name+'-panel').hidden=!open;$(name+'-toggle').setAttribute('aria-expanded',String(open));};
document.addEventListener('keydown',e=>{if(e.key==='Escape'){closeMenus();if(measure.active)setMeasuring(false);}});
$('engine').onchange=()=>switchEngine($('engine').value);
{const s=saved();if(s.engine==='three'){$('engine').value='three';switchEngine('three');}else syncStyles();}

// The viewer hands the GLB over in shared memory (or in chunks on an old
// WebView2 runtime); there is no network request for the model. The host sends
// it while the user is still picking a spot, so the model is ready when the
// panel opens; drawing waits until the panel has a size.
let incoming=null,incomingTimer=0,earlyBuffer=null;
const hasSize=()=>{const r=document.querySelector('.model-area').getBoundingClientRect();return r.width>1&&r.height>1;};
function whenSized(){return hasSize()?Promise.resolve():new Promise(resolve=>{const observer=new ResizeObserver(()=>{if(hasSize()){observer.disconnect();resolve();}});observer.observe(document.querySelector('.model-area'));});}
async function deliver(bytes){
 if(!bytes||!incoming||sceneBytes&&loaded)return;
 if(bytes.byteLength!==incoming.size){earlyBuffer=null;throw new Error('Incomplete 3D model transfer ('+bytes.byteLength+' of '+incoming.size+' bytes)');}
 clearTimeout(incomingTimer);earlyBuffer=null;sceneBytes=bytes;incoming=null;
 await whenSized();
 // Let the spinner paint before the engine blocks the thread.
 loading('Preparing '+(bytes.byteLength/1048576).toFixed(1)+' MB model…');await new Promise(r=>requestAnimationFrame(()=>setTimeout(r,0)));
 const started=performance.now();await renderer.load(sceneBytes);loaded=true;$('engine').disabled=false;if(pending)renderer.box(pending,true);
 loading(null);send({type:'loaded',ms:Math.round(performance.now()-started)});
}
bridge.addEventListener('sharedbufferreceived',async e=>{const shared=e.getBuffer();let copy=null;try{if(e.additionalData?.type==='sceneBuffer')copy=new Uint8Array(shared.slice(0));}finally{bridge.releaseBuffer?.(shared);}
 if(!copy)return;try{if(incoming)await deliver(copy);else earlyBuffer=copy;}catch(error){$('engine').disabled=false;fail(error);}});
bridge.addEventListener('message',async({data:m})=>{try{
 if(m.type==='scene'){identityByMesh=new Map((m.identities||[]).map(x=>[x.meshName,x]));identityByElement=new Map((m.identities||[]).map(x=>[x.meshName.split('/')[0],x]));$('engine').disabled=true;loading('Loading 3D model ('+(m.size/1048576).toFixed(1)+' MB)…');incoming={size:m.size,chunks:null,received:0};clearTimeout(incomingTimer);incomingTimer=setTimeout(()=>{if(incoming&&!sceneBytes)fail(new Error('The 3D model was not received from the viewer. Close and reopen 3D.'));},60000);await deliver(earlyBuffer);}
 if(m.type==='sceneChunk'){if(!incoming||m.total!==incoming.size||sceneBytes)return;incoming.chunks??=new Uint8Array(m.total);const part=Uint8Array.from(atob(m.data),c=>c.charCodeAt(0));incoming.chunks.set(part,m.offset);incoming.received+=part.length;loading('Loading 3D model… '+Math.round(100*incoming.received/m.total)+'%');if(incoming.received===m.total)await deliver(incoming.chunks);}
 if(m.type==='box'){pending=m;if(loaded){renderer.box(m,false,awaitingBoundsEcho&&!m.fitBox);if(m.fitBox){const target=renderer,fit=()=>{if(target!==renderer||!loaded)return;cancelAnimationFrame(renderer.animation);const state=renderer.fitState?.(renderer.bounds)||renderer.getCameraState();if(!renderer.fitState){state.target=renderer.bounds.min.map((v,k)=>(v+renderer.bounds.max[k])/2);state.radius=Math.max(m.width,m.depth,m.above+m.below)*(.7/.45);}renderer.setCameraState(state);};
  // On reopen the panel may still be zero wide: fit once it has its real size.
  if(hasSize())fit();else whenSized().then(()=>requestAnimationFrame(fit));}}awaitingBoundsEcho=false;}
 if(m.type==='boxDefaults'){unit=m.unit==='meters'?'meters':'feet';boxSettings={square:m.square,above:m.above,below:m.below,isDefault:m.isDefault};showBoxSettings();measure.draw();}
 if(m.type==='settingsError')status('Could not save: '+m.message,true);
 if(m.type==='docked')$('undock').textContent=m.value?'Undock':'Dock';
 }catch(e){$('engine').disabled=false;fail(e);}});
$('brightness').oninput=()=>{renderer.configure(options());save();};
$('brightness-reset').onclick=()=>{$('brightness').value='1';renderer.configure(options());save();};
for(const key of ['quality','style','shadows','occlusion','caps','edges'])$(key).onchange=()=>{renderer.configure(options());save();};
$('navigator').onchange=()=>{syncNavigator();save();};
$('reset').onclick=()=>renderer.home();$('box-reset').onclick=()=>{closeMenus();send({type:'resetBounds'});};$('hide').onclick=()=>send({type:'hide'});$('undock').onclick=()=>send({type:'undock'});
for(const type of ['pointerdown','pointermove','pointerup','wheel','keydown'])document.body.addEventListener(type,e=>{if(e.target.id==='scene')renderer.wake();});
// Grips, the box outline and the measurement overlay sit above the canvas; the
// wheel must still zoom when the pointer is over them (it used to stall there).
document.querySelector('.model-area').addEventListener('wheel',e=>{const canvas=$('scene');if(e.target===canvas||e.target.closest('.view-menu,#properties'))return;e.preventDefault();canvas.dispatchEvent(new WheelEvent('wheel',{deltaX:e.deltaX,deltaY:e.deltaY,deltaZ:e.deltaZ,deltaMode:e.deltaMode,clientX:e.clientX,clientY:e.clientY,screenX:e.screenX,screenY:e.screenY,ctrlKey:e.ctrlKey,shiftKey:e.shiftKey,altKey:e.altKey,metaKey:e.metaKey,bubbles:true,cancelable:true}));renderer.wake();},{passive:false});

let downPoint=null,lastHover=0,hoverFrame=0,hoverEvent=null;
function properties(hit){const mesh=hit?.pickedMesh;if(!mesh)return;selected=mesh.name;renderer.select?.(selected);const id=identityByMesh.get(mesh.name)||identityByElement.get(mesh.name.split('/')[0])||mesh.metadata?.revit||{};const values=[['Element ID',id.elementId],['Unique ID',id.uniqueId],['Category',id.category],['Family',id.familyName],['Type',id.typeName],['Level',id.levelName],['Mark',id.mark],['Material',mesh.metadata?.original?.name||mesh.userData?.original?.name||id.materialName],['Link',id.linkInstanceId],...Object.entries(id.parameters||{})];const dl=document.createElement('dl');for(const [name,value]of values){if(!value)continue;const dt=document.createElement('dt'),dd=document.createElement('dd');dt.textContent=name;dd.textContent=String(value);dl.append(dt,dd);}$('property-values').replaceChildren(dl);$('properties').hidden=false;renderer.wake();}
function setMeasuring(on){if(on){closeMenus();measure.start();}else measure.stop();$('measure').textContent=on?'Stop measuring':'Measure';$('measure').setAttribute('aria-pressed',String(on));document.body.classList.toggle('measuring',on);$('hint').textContent=on?'Measure: click two points · corners snap · hold Shift to keep along X, Y or Z · Esc to finish':'';}
document.body.addEventListener('pointerdown',e=>{if(e.target.id==='scene'){closeMenus();downPoint=[e.clientX,e.clientY];}});
document.body.addEventListener('pointerup',e=>{if(e.target.id!=='scene'||e.button!==0||!downPoint||Math.hypot(e.clientX-downPoint[0],e.clientY-downPoint[1])>4)return;if(measure.active){measure.click(e);return;}const hit=renderer.pick(e);if(!hit){clearSelection();return;}properties(hit);});
document.body.addEventListener('pointermove',e=>{if(e.target.id!=='scene')return;if(measure.active){hoverEvent=e;if(!hoverFrame)hoverFrame=requestAnimationFrame(()=>{hoverFrame=0;if(hoverEvent&&hoverEvent.buttons===0)measure.move(hoverEvent);});return;}if(e.buttons===0&&performance.now()-lastHover>150){lastHover=performance.now();const hit=renderer.pick(e);const id=hit&&(identityByMesh.get(hit.pickedMesh.name)||identityByElement.get(hit.pickedMesh.name.split('/')[0]));$('scene').title=id?[id.category,id.familyName,id.typeName,id.mark].filter(Boolean).join(' · '):'';}});
document.body.addEventListener('dblclick',e=>{if(e.target.id==='scene'&&!measure.active){const hit=renderer.pick(e);if(hit){properties(hit);renderer.focus?.(selected,hit);}}});
function clearSelection(){selected=null;renderer.select?.(null);renderer.orbitSelection=null;$('properties').hidden=true;}
$('clear-selection').onclick=clearSelection;$('close-properties').onclick=()=>{$('properties').hidden=true;};
$('focus-selection').onclick=()=>selected&&renderer.focus?.(selected);
$('hide-selection').onclick=()=>selected&&renderer.visibility?.('hide',selected);
$('isolate-selection').onclick=()=>selected&&renderer.visibility?.('isolate',selected);
$('xray-selection').onclick=()=>selected&&renderer.visibility?.('xray',selected);
$('show-all').onclick=()=>renderer.visibility?.('all',null);
$('measure').onclick=()=>setMeasuring(!measure.active);
window.addEventListener('resize',()=>{renderer.wake();measure.draw();});window.addEventListener('pagehide',()=>renderer.dispose());window.addEventListener('unhandledrejection',e=>fail(e.reason));
const views={front:[Math.PI/2,Math.PI/2],back:[-Math.PI/2,Math.PI/2],right:[0,Math.PI/2],left:[Math.PI,Math.PI/2],top:[-Math.PI/2,.000001],bottom:[-Math.PI/2,Math.PI-.000001]};
let cubeKey='';
function updateCube(state){if(!state)return;const key=[state.alpha,state.beta].map(v=>v.toFixed(5)).join();if(key===cubeKey)return;cubeKey=key;document.querySelector('.cube-body').style.transform=`rotateX(${(state.beta-Math.PI/2)*180/Math.PI}deg) rotateY(${(state.alpha-Math.PI/2)*180/Math.PI}deg)`;}
function standardView(name){const state=renderer.getCameraState?.(),preset=views[name];if(!state||!preset)return;state.alpha=preset[0];state.beta=preset[1];renderer.setCameraState(state);updateCube(state);}
const cube=document.querySelector('.view-cube');let cubeDrag,suppressCubeClick=false;
cube.addEventListener('pointerdown',e=>{const state=renderer.getCameraState?.();if(!state)return;cubeDrag={x:e.clientX,y:e.clientY,state,moved:false,face:e.target.closest('[data-view]')?.dataset.view};cube.setPointerCapture(e.pointerId);});
// Same direction as orbiting the model: the cube turns with the pointer.
cube.addEventListener('pointermove',e=>{if(!cubeDrag)return;const d=cubeDrag,dx=e.clientX-d.x,dy=e.clientY-d.y;if(Math.hypot(dx,dy)>4)d.moved=true;if(!d.moved)return;const state={...d.state,alpha:d.state.alpha+dx*.012,beta:Math.max(.000001,Math.min(Math.PI-.000001,d.state.beta-dy*.012))};renderer.setCameraState(state);updateCube(state);});
cube.addEventListener('pointerup',e=>{suppressCubeClick=!!cubeDrag?.moved;if(cubeDrag&&!cubeDrag.moved&&cubeDrag.face)standardView(cubeDrag.face);cubeDrag=null;if(cube.hasPointerCapture(e.pointerId))cube.releasePointerCapture(e.pointerId);setTimeout(()=>suppressCubeClick=false,0);});
cube.addEventListener('keydown',e=>{if(e.key==='PageUp'||e.key==='PageDown'){standardView(e.key==='PageUp'?'top':'bottom');e.preventDefault();return;}if(e.key==='Home'){renderer.home();e.preventDefault();return;}const delta={ArrowLeft:[-.25,0],ArrowRight:[.25,0],ArrowUp:[0,-.25],ArrowDown:[0,.25]}[e.key],state=renderer.getCameraState?.();if(delta&&state){state.alpha+=delta[0];state.beta=Math.max(.000001,Math.min(Math.PI-.000001,state.beta+delta[1]));renderer.setCameraState(state);updateCube(state);e.preventDefault();}});
cube.addEventListener('click',e=>{const face=e.target.closest('[data-view]');if(face&&!suppressCubeClick&&e.detail===0)standardView(face.dataset.view);});
export const testing={get renderer(){return renderer;},switchEngine,measure};send({type:'ready'});

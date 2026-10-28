import * as pdfjs from './vendor/pdf.mjs';
pdfjs.GlobalWorkerOptions.workerSrc=new URL('./vendor/pdf.worker.mjs',import.meta.url).href;
const $=id=>document.getElementById(id), bridge=window.chrome?.webview;
const send=value=>bridge?.postMessage(value),error=e=>{send({type:'error',message:String(e.message||e)});$('hint').textContent=String(e.message||e);};
window.addEventListener('unhandledrejection',e=>error(e.reason));
const decode=s=>Uint8Array.from(atob(s),c=>c.charCodeAt(0));
// Replaceable adapters: the host contract uses raw PDF default user space and local-origin metres.
export class PdfJsAdapter {
 async open(base64){await this.dispose();this.task=pdfjs.getDocument({data:decode(base64),isEvalSupported:false,enableXfa:false,useSystemFonts:false,disableFontFace:true});this.pdf=await this.task.promise;this.page=await this.pdf.getPage(1);await this.render();}
 async render(){if(!this.page)return;if(this.rendering){this.rendering.cancel();try{await this.rendering.promise;}catch{}}
 this.viewport=this.page.getViewport({scale:Number($('zoom').value)/100});const c=$('pdf'),dpr=devicePixelRatio;c.width=Math.ceil(this.viewport.width*dpr);c.height=Math.ceil(this.viewport.height*dpr);c.style.width=this.viewport.width+'px';c.style.height=this.viewport.height+'px';this.rendering=this.page.render({canvasContext:c.getContext('2d'),viewport:this.viewport,transform:[dpr,0,0,dpr,0,0]});await this.rendering.promise;}
 point(event){const rect=$('pdf').getBoundingClientRect();return this.viewport.convertToPdfPoint((event.clientX-rect.left)*this.viewport.width/rect.width,(event.clientY-rect.top)*this.viewport.height/rect.height);}
 async dispose(){this.rendering?.cancel();if(this.task)await this.task.destroy();this.page=null;this.task=null;}
}
import {BabylonAdapter} from './renderer.mjs';

const pdf=new PdfJsAdapter(), renderer=new BabylonAdapter();let boxPending=null,loadPromise=null;
const dimensions=()=>({width:Number($('width').value),above:Number($('above').value),below:Number($('below').value)});
bridge?.addEventListener('message',async event=>{try{const m=event.data;
 if(m.type==='document'){renderer.dispose();loadPromise=null;boxPending=null;$('marker').hidden=true;$('panel').style.visibility='visible';$('kind').textContent=m.synthetic?'SYNTHETIC DEMO':m.registered?'Verified package':'2D / calibration mode';$('empty').hidden=true;await pdf.open(m.pdf);}
 if(m.type==='scene'){loadPromise=renderer.load(decode(m.glb));await loadPromise;if(boxPending)renderer.box(boxPending,true);}
 if(m.type==='box'){boxPending=m;$('panel').style.visibility='visible';renderer.box(m);}
 }catch(e){error(e);}});
$('pdf').addEventListener('click',e=>{if(!pdf.viewport)return;const [x,y]=pdf.point(e);$('coordinate').value=JSON.stringify({page:1,pdfX:x,pdfY:y});const rect=$('pdf').getBoundingClientRect();$('marker').style.left=e.clientX-rect.left+'px';$('marker').style.top=e.clientY-rect.top+'px';$('marker').hidden=false;send({type:'click',page:1,x,y,...dimensions()});});
$('zoom').addEventListener('change',()=>pdf.render().catch(error));$('fit').addEventListener('click',()=>{if(!pdf.page)return;const v=pdf.page.getViewport({scale:1});$('zoom').value=Math.max(25,Math.min(300,Math.floor(($('paperScroll').clientWidth-40)/v.width*100)));pdf.render().catch(error);});
for(const id of ['width','above','below'])$(id).addEventListener('change',()=>send({type:'size',...dimensions()}));
$('reset').onclick=()=>renderer.reset();$('hide').onclick=()=>{$('panel').style.visibility='hidden';renderer.active=false;};
for(const type of ['pointerdown','pointermove','wheel','keydown'])$('scene').addEventListener(type,()=>renderer.wake());
$('scene').addEventListener('pointerup',e=>{const hit=renderer.pick(e);if(hit)$('hint').textContent='Element: '+hit.pickedMesh.name;renderer.wake();});
window.addEventListener('resize',()=>renderer.wake());window.addEventListener('pagehide',()=>{pdf.dispose();renderer.dispose();});
// Exposed only to local developer harness; no remote scripts or executable attachments are accepted.
export const testing={pdf,renderer};send({type:'ready'});

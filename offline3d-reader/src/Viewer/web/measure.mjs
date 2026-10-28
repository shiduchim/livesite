// Point-to-point measuring on the model. Points snap to the nearest corner
// (mesh vertex) of the element under the pointer; otherwise they sit on the
// surface. Shift keeps the second point along the dominant X, Y or Z axis.
// The result is drawn in an SVG layer and follows the model as the view moves.
const ns='http://www.w3.org/2000/svg',snapPixels=12;

/** Metres as Revit-style feet-inches (nearest 1/16") or metres. */
export function formatLength(metres,unit){
 if(!Number.isFinite(metres))return '';
 if(unit==='meters')return Math.abs(metres)<1?(metres*1000).toFixed(0)+' mm':metres.toFixed(3)+' m';
 const sixteenths=Math.round(Math.abs(metres)/.0254*16),feet=Math.floor(sixteenths/192),rest=sixteenths-feet*192,inches=Math.floor(rest/16);let fraction=rest-inches*16,denominator=16;
 while(fraction&&fraction%2===0){fraction/=2;denominator/=2;}
 return `${metres<0?'-':''}${feet}'-${inches}${fraction?` ${fraction}/${denominator}`:''}"`;
}

export class MeasureTool{
 constructor(getRenderer,getUnit){this.getRenderer=getRenderer;this.getUnit=getUnit;this.layer=document.getElementById('measure-layer');this.active=false;this.a=null;this.b=null;this.hover=null;this.cache=new WeakMap();}
 start(){this.active=true;this.a=this.b=this.hover=null;this.draw();}
 stop(){this.active=false;this.a=this.b=this.hover=null;this.draw();}
 move(e){if(!this.active)return;this.hover=this.snap(e);if(this.hover&&this.a&&!this.b&&e.shiftKey)this.hover={...this.hover,point:this.constrain(this.a,this.hover.point),vertex:false};this.draw();}
 click(e){if(!this.active)return;const s=this.snap(e);if(!s)return;if(!this.a||this.b){this.a=s.point;this.b=null;}else this.b=e.shiftKey?this.constrain(this.a,s.point):s.point;this.hover=null;this.draw();}
 constrain(a,p){const d=p.map((v,k)=>v-a[k]),axis=[0,1,2].reduce((m,k)=>Math.abs(d[k])>Math.abs(d[m])?k:m,0);return a.map((v,k)=>k===axis?p[k]:v);}
 // World size of one screen pixel in the orthographic view.
 pixel(){const r=this.getRenderer(),state=r.getCameraState?.(),canvas=document.getElementById('scene');return state?state.radius*.9/Math.max(1,canvas.clientHeight):0;}
 snap(e){
  const r=this.getRenderer();if(!r?.loaded)return null;const hit=r.pick({clientX:e.clientX,clientY:e.clientY,buttons:0});if(!hit?.pickedPoint)return null;
  const p=hit.pickedPoint,point=p.asArray?p.asArray():[p.x,p.y,p.z],limit=snapPixels*this.pixel();
  let best=null,bestDistance=limit;const vertices=this.vertices(r,hit.rawMesh||hit.pickedMesh);
  if(vertices){const {positions,matrix:m}=vertices,inside=r.bounds;for(let i=0;i<positions.length;i+=3){const x=positions[i],y=positions[i+1],z=positions[i+2],w=[m[0]*x+m[4]*y+m[8]*z+m[12],m[1]*x+m[5]*y+m[9]*z+m[13],m[2]*x+m[6]*y+m[10]*z+m[14]],d=Math.hypot(w[0]-point[0],w[1]-point[1],w[2]-point[2]);if(d<bestDistance&&(!inside||w.every((v,k)=>v>=inside.min[k]-1e-6&&v<=inside.max[k]+1e-6))){bestDistance=d;best=w;}}}
  return best?{point:best,vertex:true}:{point,vertex:false};
 }
 // Positions (local) and world matrix of a picked mesh, cached per mesh.
 vertices(r,mesh){if(!mesh||!r.meshVertices)return null;let v=this.cache.get(mesh);if(v===undefined){try{v=r.meshVertices(mesh);}catch{v=null;}if(v&&v.positions.length>3*400000)v=null;this.cache.set(mesh,v);}return v?{positions:v.positions,matrix:r.meshMatrix?r.meshMatrix(mesh):v.matrix}:null;}
 draw(){
  const layer=this.layer,r=this.getRenderer();if(!layer)return;layer.replaceChildren();
  if(!this.active||!r?.loaded||!r.project)return;const area=layer.getBoundingClientRect(),screen=p=>{const q=r.project(p);return[q.x-area.left,q.y-area.top];};
  const el=(name,attrs,text)=>{const node=document.createElementNS(ns,name);for(const [k,v] of Object.entries(attrs))node.setAttribute(k,v);if(text!==undefined)node.textContent=text;layer.append(node);return node;};
  const marker=(p,vertex)=>{const [x,y]=screen(p);if(vertex)el('rect',{x:x-4.5,y:y-4.5,width:9,height:9,class:'snap'});else el('circle',{cx:x,cy:y,r:3.5,class:'point'});};
  const end=this.b||(this.a&&this.hover?.point);
  if(this.a&&end){
   const [x1,y1]=screen(this.a),[x2,y2]=screen(end),d=end.map((v,k)=>v-this.a[k]),unit=this.getUnit(),total=Math.hypot(...d);
   el('line',{x1,y1,x2,y2,class:this.b?'line':'line preview'});marker(this.a,false);marker(end,!this.b&&this.hover?.vertex);
   const detail=`Horizontal ${formatLength(Math.hypot(d[0],d[2]),unit)} · Vertical ${formatLength(Math.abs(d[1]),unit)}`,mx=(x1+x2)/2,my=(y1+y2)/2;
   const label=el('g',{class:'label',transform:`translate(${Math.round(mx)},${Math.round(my)-14})`});const bg=document.createElementNS(ns,'rect'),t1=document.createElementNS(ns,'text'),t2=document.createElementNS(ns,'text');t1.textContent=formatLength(total,unit);t1.setAttribute('class','distance');t1.setAttribute('y','-3');t2.textContent=detail;t2.setAttribute('class','detail');t2.setAttribute('y','10');label.append(bg,t1,t2);
   const w=Math.max(t1.getComputedTextLength(),t2.getComputedTextLength())+12;bg.setAttribute('x',-w/2);bg.setAttribute('y','-17');bg.setAttribute('width',w);bg.setAttribute('height','32');bg.setAttribute('rx','3');
  }else if(this.hover)marker(this.hover.point,this.hover.vertex);
 }
}

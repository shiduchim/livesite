// Local section contours. Geometry is grouped by element across painted faces.
export function sectionLoops(triangles,axis,value,epsilon=1e-6){
 const other=[0,1,2].filter(k=>k!==axis),points=new Map(),edges=new Map(),key=p=>other.map(k=>Math.round(p[k]/epsilon)).join(',');
 const visit=v=>{const d=v.map(p=>p[axis]-value);if(d.every(x=>Math.abs(x)<epsilon)||d.every(x=>x>epsilon)||d.every(x=>x< -epsilon))return;
  const hits=[];for(let j=0;j<3;j++){const a=v[j],b=v[(j+1)%3],da=d[j],db=d[(j+1)%3];if(Math.abs(da)<epsilon)hits.push(a);if(da*db< -epsilon*epsilon){const t=da/(da-db);hits.push(a.map((x,k)=>x+t*(b[k]-x)));}}
  const unique=[...new Map(hits.map(p=>[key(p),p])).entries()];if(unique.length!==2)return;const [[a,pa],[b,pb]]=unique;if(a===b)return;points.set(a,pa);points.set(b,pb);edges.set([a,b].sort().join('|'),[a,b]);
 };if(typeof triangles==='function')triangles(visit);else for(let i=0;i<triangles.length;i+=9)visit([0,3,6].map(j=>[triangles[i+j],triangles[i+j+1],triangles[i+j+2]]));
 const adjacent=new Map();for(const [a,b] of edges.values()){if(!adjacent.has(a))adjacent.set(a,[]);if(!adjacent.has(b))adjacent.set(b,[]);adjacent.get(a).push(b);adjacent.get(b).push(a);}
 const visited=new Set(),loops=[];let open=0;
 for(const start of adjacent.keys()){if(visited.has(start))continue;const component=[],queue=[start];visited.add(start);while(queue.length){const a=queue.pop();component.push(a);for(const b of adjacent.get(a))if(!visited.has(b)){visited.add(b);queue.push(b);}}
  if(component.some(k=>adjacent.get(k).length!==2)){open++;continue;}
  const loop=[];let previous=null,current=start;do{loop.push(points.get(current));const next=adjacent.get(current).find(k=>k!==previous);previous=current;current=next;}while(current!==start&&loop.length<=component.length);
  if(current===start&&loop.length>=3)loops.push(loop.map(p=>other.map(k=>p[k])));else open++;
 }return {loops,open,other};
}
const area=ring=>Math.abs(ring.reduce((s,p,i)=>{const q=ring[(i+1)%ring.length];return s+p[0]*q[1]-q[0]*p[1];},0)/2);
const inside=(p,ring)=>{let result=false;for(let i=0,j=ring.length-1;i<ring.length;j=i++){const a=ring[i],b=ring[j];if((a[1]>p[1])!==(b[1]>p[1])&&p[0]<(b[0]-a[0])*(p[1]-a[1])/(b[1]-a[1])+a[0])result=!result;}return result;};
export function triangulateSection(loops,earcut){
 const rings=loops.map(points=>({points,area:area(points),parent:-1,depth:0})).filter(r=>r.area>1e-12).sort((a,b)=>b.area-a.area);
 for(let i=0;i<rings.length;i++)for(let j=i-1;j>=0;j--)if(inside(rings[i].points[0],rings[j].points)){rings[i].parent=j;rings[i].depth=rings[j].depth+1;break;}
 const output=[];for(let i=0;i<rings.length;i++){if(rings[i].depth%2)continue;const holes=[],flat=rings[i].points.flat();for(let j=i+1;j<rings.length;j++)if(rings[j].parent===i&&rings[j].depth%2){holes.push(flat.length/2);flat.push(...rings[j].points.flat());}for(const n of earcut(flat,holes,2))output.push(flat[n*2],flat[n*2+1]);}return output;
}
// Revit cut patterns on section caps. Each fill grid is a family of parallel lines:
// angle (radians), origin, perpendicular spacing (offset), along-line shift per line
// and dash segments (alternating dash and gap, feet). Model patterns are real size;
// drafting patterns are drawn at 1/4" = 1'-0" (x48). Lines are clipped to the cap's
// triangles. Returns flat [x0,y0,x1,y1,...] in the cap's 2D coordinates (metres).
const FEET=.3048,DRAFTING_SCALE=48,MAX_LINES_PER_GRID=700,MAX_SEGMENTS=6000;
export function hatchSegments(flat,pattern,toPattern=(x,y)=>[x,y]){
 if(!pattern||pattern.solid||!Array.isArray(pattern.grids)||!pattern.grids.length||flat.length<6)return[];
 const scale=(/draft/i.test(pattern.target||'')?DRAFTING_SCALE:1)*FEET,out=[];
 const tris=[];for(let i=0;i+5<flat.length;i+=6)tris.push([toPattern(flat[i],flat[i+1]),toPattern(flat[i+2],flat[i+3]),toPattern(flat[i+4],flat[i+5])]);
 for(const g of pattern.grids){
  const spacing=Math.abs(g.offsetFeet)*scale;if(!(spacing>1e-6))continue;
  const a=g.angle||0,d=[Math.cos(a),Math.sin(a)],n=[-d[1],d[0]],o=[(g.originXFeet||0)*scale,(g.originYFeet||0)*scale],shift=(g.shiftFeet||0)*scale;
  const dashes=(g.segmentsFeet||[]).map(v=>Math.abs(v)*scale).filter(v=>v>0),period=dashes.reduce((t,v)=>t+v,0);
  let lo=Infinity,hi=-Infinity;for(const t of tris)for(const p of t){const v=(p[0]-o[0])*n[0]+(p[1]-o[1])*n[1];lo=Math.min(lo,v);hi=Math.max(hi,v);}
  const k0=Math.ceil(lo/spacing),k1=Math.floor(hi/spacing);if(k1<k0||k1-k0>MAX_LINES_PER_GRID)continue;
  for(let k=k0;k<=k1;k++){
   const c=k*spacing,intervals=[];
   for(const t of tris){const f=t.map(p=>(p[0]-o[0])*n[0]+(p[1]-o[1])*n[1]-c),hits=[];
    for(let e=0;e<3;e++){const i=e,j=(e+1)%3;if((f[i]<0)!==(f[j]<0)&&f[i]!==f[j]){const u=f[i]/(f[i]-f[j]);hits.push([t[i][0]+u*(t[j][0]-t[i][0]),t[i][1]+u*(t[j][1]-t[i][1])]);}else if(f[i]===0)hits.push(t[i]);}
    if(hits.length<2)continue;const along=hits.map(p=>(p[0]-o[0])*d[0]+(p[1]-o[1])*d[1]);intervals.push([Math.min(...along),Math.max(...along)]);}
   if(!intervals.length)continue;intervals.sort((x,y)=>x[0]-y[0]);const merged=[intervals[0].slice()];
   for(const [s0,s1] of intervals.slice(1)){const last=merged[merged.length-1];if(s0<=last[1]+1e-9)last[1]=Math.max(last[1],s1);else merged.push([s0,s1]);}
   const point=s=>{const p=[o[0]+d[0]*s+n[0]*c,o[1]+d[1]*s+n[1]*c];return toPattern.inverse?toPattern.inverse(p[0],p[1]):p;};
   const emit=(s0,s1)=>{if(s1-s0<1e-6)return;const p=point(s0),q=point(s1);out.push(p[0],p[1],q[0],q[1]);};
   for(const [s0,s1] of merged){
    if(!period||dashes.length<2){emit(s0,s1);}
    else{const start=k*shift;let s=s0-((((s0-start)%period)+period)%period),i=0;
     while(s<s1&&out.length<MAX_SEGMENTS*4){const len=dashes[i%dashes.length];if(i%2===0)emit(Math.max(s,s0),Math.min(s+len,s1));s+=len;i++;}}
    if(out.length>=MAX_SEGMENTS*4)return out;
   }
  }
 }
 return out;
}
export class SectionCaps {
 constructor(scene,models,bytes){this.scene=scene;this.caps=[];this.groups=new Map();this.materials=new Map();this.lineMaterial=null;this.showOutlines=true;this.request=0;
  const patterns=materialPatterns(bytes);this.patterns=true;
  for(const mesh of models){const key=mesh.name.split('/')[0];if(this.groups.has(key))continue;const original=mesh.metadata?.original||mesh.material;this.groups.set(key,{material:mesh.material,pattern:original?.metadata?.gltf?.extras?.cutPattern||patterns.get(original?.name)});}
  this.worker=new Worker(new URL('./section-worker.mjs',import.meta.url),{type:'module'});const copy=bytes.slice();this.worker.postMessage({type:'init',bytes:copy},[copy.buffer]);
  this.worker.onmessage=({data:m})=>{if(m.id!==this.request||this.scene.isDisposed)return;this.clear();if(m.error){this.onError?.(m.error);return;}this.workStats={candidates:m.candidates,trianglesVisited:m.trianglesVisited};this.openContours=m.open;this.apply(m.bounds,m.caps);this.onChanged?.();};
  this.worker.onerror=e=>this.onError?.(e.message||'Section worker failed');
 }
 // Outlines share one line material and caps share materials, so neither is disposed with a cap.
 clear(){for(const m of this.caps){m.metadata.outline?.dispose(false,false,true);m.metadata.hatch?.dispose(false,false,true);m.dispose(false,false);}this.caps=[];}
 capMaterial(material){if(!('roughness' in material))return material;let clone=this.materials.get(material);if(!clone){clone=material.clone('Section material');clone.backFaceCulling=false;clone.roughness=Math.max(.65,clone.roughness??1);this.materials.set(material,clone);}return clone;}
 rebuild(bounds,enabled=true){this.request++;this.clear();if(enabled)this.worker.postMessage({type:'cut',id:this.request,bounds});}
 apply(bounds,caps){const B=window.BABYLON;for(const item of caps){const {axis,side,flat,other,key}=item,value=bounds[side][axis],group=this.groups.get(key);if(!group)continue;
   const positions=[],normals=[],indices=[];for(let i=0;i<flat.length;i+=2){const p=[0,0,0],n=[0,0,0];p[axis]=value+(side==='min'?1:-1)*1e-5;p[other[0]]=flat[i];p[other[1]]=flat[i+1];n[axis]=side==='min'?-1:1;positions.push(...p);normals.push(...n);indices.push(indices.length);}
   const cap=new B.Mesh('Solid section',this.scene),data=new B.VertexData();data.positions=positions;data.normals=normals;data.indices=indices;data.applyToMesh(cap);cap.material=this.capMaterial(group.material);cap.metadata={owner:key,outline:this.showOutlines?this.outline(cap,positions):null,hatch:this.patterns?this.hatch(cap,item,value,group.pattern):null};cap.isPickable=true;cap.receiveShadows=true;this.caps.push(cap);
  }
 }
 // Revit draws the cut boundary: the cap triangles' edges that only one triangle uses.
 outline(cap,positions){const B=window.BABYLON,count=new Map(),key=i=>positions[i*3]+','+positions[i*3+1]+','+positions[i*3+2];for(let t=0;t<positions.length/3;t+=3)for(let k=0;k<3;k++){const a=t+k,b=t+(k+1)%3,ka=key(a),kb=key(b),edge=ka<kb?ka+'|'+kb:kb+'|'+ka;count.set(edge,count.has(edge)?null:[a,b]);}
  const points=[];for(const pair of count.values())if(pair)for(const i of pair)points.push(positions[i*3],positions[i*3+1],positions[i*3+2]);if(!points.length)return null;
  const line=new B.LinesMesh('Section outline',this.scene,cap,null,false,false,false,this.lineMaterial||undefined);this.lineMaterial??=line.material;const data=new B.VertexData();data.positions=points;data.indices=Array.from({length:points.length/3},(_,i)=>i);data.applyToMesh(line);line.color=new B.Color3(.1,.1,.1);line.isPickable=false;return line;}
 // Pattern lines sit between the cap and the box face (caps are 1e-5 inside, lines 0.5e-5).
 // Pattern axes: plan caps use east (x) and north (-z); vertical caps use the horizontal axis and up (y).
 hatch(cap,item,value,pattern){if(!pattern)return null;const B=window.BABYLON,{axis,side,flat,other}=item,flipV=axis===1&&other[1]===2;
  const map=(x,y)=>[x,flipV?-y:y];map.inverse=(u,v)=>[u,flipV?-v:v];const segments=hatchSegments(flat,pattern,map);if(!segments.length)return null;
  const points=[],offset=value+(side==='min'?1:-1)*.5e-5;for(let i=0;i<segments.length;i+=2){const p=[0,0,0];p[axis]=offset;p[other[0]]=segments[i];p[other[1]]=segments[i+1];points.push(...p);}
  const line=new B.LinesMesh('Cut pattern',this.scene,cap,null,false,false,false,this.patternMaterial||undefined);this.patternMaterial??=line.material;const data=new B.VertexData();data.positions=points;data.indices=Array.from({length:points.length/3},(_,i)=>i);data.applyToMesh(line);line.color=new B.Color3(.18,.18,.18);line.isPickable=false;return line;}
 dispose(){this.worker?.terminate();this.patternMaterial?.dispose();this.patternMaterial=null;this.request++;this.clear();this.groups.clear();for(const material of this.materials.values())material.dispose();this.materials.clear();this.lineMaterial?.dispose();this.lineMaterial=null;}
}

// Material name -> cut pattern from the GLB JSON (fallback when the loader drops extras).
function materialPatterns(bytes){const result=new Map();try{const view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength),length=view.getUint32(12,true),json=JSON.parse(new TextDecoder().decode(bytes.subarray(20,20+length)));for(const m of json.materials||[])if(m.extras?.cutPattern&&!result.has(m.name))result.set(m.name,m.extras.cutPattern);}catch{}return result;}

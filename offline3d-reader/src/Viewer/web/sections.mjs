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
export class SectionCaps {
 constructor(scene,models,bytes){this.scene=scene;this.caps=[];this.groups=new Map();this.materials=new Map();this.lineMaterial=null;this.showOutlines=true;this.request=0;
  for(const mesh of models){const key=mesh.name.split('/')[0];if(!this.groups.has(key))this.groups.set(key,{material:mesh.material});}
  this.worker=new Worker(new URL('./section-worker.mjs',import.meta.url),{type:'module'});const copy=bytes.slice();this.worker.postMessage({type:'init',bytes:copy},[copy.buffer]);
  this.worker.onmessage=({data:m})=>{if(m.id!==this.request||this.scene.isDisposed)return;this.clear();if(m.error){this.onError?.(m.error);return;}this.workStats={candidates:m.candidates,trianglesVisited:m.trianglesVisited};this.openContours=m.open;this.apply(m.bounds,m.caps);this.onChanged?.();};
  this.worker.onerror=e=>this.onError?.(e.message||'Section worker failed');
 }
 // Outlines share one line material and caps share materials, so neither is disposed with a cap.
 clear(){for(const m of this.caps){m.metadata.outline?.dispose(false,false,true);m.dispose(false,false);}this.caps=[];}
 capMaterial(material){if(!('roughness' in material))return material;let clone=this.materials.get(material);if(!clone){clone=material.clone('Section material');clone.backFaceCulling=false;clone.roughness=Math.max(.65,clone.roughness??1);this.materials.set(material,clone);}return clone;}
 rebuild(bounds,enabled=true){this.request++;this.clear();if(enabled)this.worker.postMessage({type:'cut',id:this.request,bounds});}
 apply(bounds,caps){const B=window.BABYLON;for(const item of caps){const {axis,side,flat,other,key}=item,value=bounds[side][axis],group=this.groups.get(key);if(!group)continue;
   const positions=[],normals=[],indices=[];for(let i=0;i<flat.length;i+=2){const p=[0,0,0],n=[0,0,0];p[axis]=value+(side==='min'?1:-1)*1e-5;p[other[0]]=flat[i];p[other[1]]=flat[i+1];n[axis]=side==='min'?-1:1;positions.push(...p);normals.push(...n);indices.push(indices.length);}
   const cap=new B.Mesh('Solid section',this.scene),data=new B.VertexData();data.positions=positions;data.normals=normals;data.indices=indices;data.applyToMesh(cap);cap.material=this.capMaterial(group.material);cap.metadata={owner:key,outline:this.showOutlines?this.outline(cap,positions):null};cap.isPickable=true;cap.receiveShadows=true;this.caps.push(cap);
  }
 }
 // Revit draws the cut boundary: the cap triangles' edges that only one triangle uses.
 outline(cap,positions){const B=window.BABYLON,count=new Map(),key=i=>positions[i*3]+','+positions[i*3+1]+','+positions[i*3+2];for(let t=0;t<positions.length/3;t+=3)for(let k=0;k<3;k++){const a=t+k,b=t+(k+1)%3,ka=key(a),kb=key(b),edge=ka<kb?ka+'|'+kb:kb+'|'+ka;count.set(edge,count.has(edge)?null:[a,b]);}
  const points=[];for(const pair of count.values())if(pair)for(const i of pair)points.push(positions[i*3],positions[i*3+1],positions[i*3+2]);if(!points.length)return null;
  const line=new B.LinesMesh('Section outline',this.scene,cap,null,false,false,false,this.lineMaterial||undefined);this.lineMaterial??=line.material;const data=new B.VertexData();data.positions=points;data.indices=Array.from({length:points.length/3},(_,i)=>i);data.applyToMesh(line);line.color=new B.Color3(.1,.1,.1);line.isPickable=false;return line;}
 dispose(){this.worker?.terminate();this.request++;this.clear();this.groups.clear();for(const material of this.materials.values())material.dispose();this.materials.clear();this.lineMaterial?.dispose();this.lineMaterial=null;}
}

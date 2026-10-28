import './vendor/earcut.js';
import {sectionLoops,triangulateSection} from './sections.mjs';
import {parseScene,intersects,visitTriangles} from './scene-data.mjs';
let scene,ticket=0;
self.onmessage=({data:m})=>{if(m.type==='init'){scene=parseScene(m.bytes instanceof Uint8Array?m.bytes:new Uint8Array(m.bytes));ticket++;return;}if(m.type==='cut'){const current=++ticket;cut(m,current).catch(e=>{if(current===ticket)self.postMessage({id:m.id,error:String(e.message||e)});});}};
async function cut(m,current){if(!scene)return;const caps=[];let open=0,candidates=0,trianglesVisited=0,steps=0;
 for(let axis=0;axis<3;axis++)for(const side of ['min','max']){const value=m.bounds[side][axis],groups=new Map();
  for(const node of scene.nodes){if(!intersects(node,m.bounds)||value<=node.min[axis]+1e-6||value>=node.max[axis]-1e-6)continue;if(!groups.has(node.key))groups.set(node.key,[]);groups.get(node.key).push(node);candidates++;}
  for(const [key,nodes]of groups){if(current!==ticket)return;trianglesVisited+=nodes.reduce((n,node)=>n+scene.meshes[node.mesh].triangles,0);if(trianglesVisited>4000000)throw Error('Solid cuts paused: this box crosses more than four million triangles. Narrow the box for solid cut faces.');
   const result=sectionLoops(visit=>visitTriangles(scene,nodes,visit),axis,value);open+=result.open;const flat=new Float64Array(triangulateSection(result.loops,self.earcut));if(flat.length)caps.push({key,axis,side,other:result.other,flat});
   if(++steps%16===0)await new Promise(resolve=>setTimeout(resolve,0));
  }
 }
 if(current===ticket)self.postMessage({id:m.id,bounds:m.bounds,caps,open,candidates,trianglesVisited},caps.map(c=>c.flat.buffer));
}

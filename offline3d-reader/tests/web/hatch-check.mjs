// Node check of the cut-pattern lines drawn on section caps (sections.mjs hatchSegments).
// Usage: node hatch-check.mjs
import {hatchSegments} from '../../src/Viewer/web/sections.mjs';
let failed=0;const check=(name,ok,detail='')=>{console.log((ok?'PASS ':'FAIL ')+name+(detail?' — '+detail:''));if(!ok)failed++;};
const FT=.3048;
// A 1 m square as two triangles.
const square=[0,0,1,0,1,1, 0,0,1,1,0,1];
const lines=(segments)=>segments.length/4;
const len=(s,i)=>Math.hypot(s[i+2]-s[i],s[i+3]-s[i+1]);
const horizontal={name:'h',target:'Model',solid:false,grids:[{angle:0,originXFeet:0,originYFeet:0,offsetFeet:.1/FT,shiftFeet:0,segmentsFeet:[]}]};
let s=hatchSegments(square,horizontal);
check('horizontal lines every 0.1 m across a 1 m square (shared triangle edges merged)',lines(s)===11,String(lines(s)));
check('each line spans the full square',[...Array(lines(s)).keys()].every(i=>Math.abs(len(s,i*4)-1)<1e-9));
const diagonal={...horizontal,grids:[{...horizontal.grids[0],angle:Math.PI/4}]};s=hatchSegments(square,diagonal);
check('45° lines stay inside the square',s.every(v=>v>-1e-9&&v<1+1e-9)&&lines(s)>=13&&lines(s)<=15,String(lines(s)));
const dashed={...horizontal,grids:[{...horizontal.grids[0],segmentsFeet:[.1/FT,.1/FT]}]};s=hatchSegments(square,dashed);
check('dash 0.1 m / gap 0.1 m gives 5 dashes per line',lines(s)===55&&[...Array(lines(s)).keys()].every(i=>len(s,i*4)<=.1+1e-9),String(lines(s)));
const drafting={...horizontal,target:'Drafting',grids:[{...horizontal.grids[0],offsetFeet:.01/FT}]};s=hatchSegments(square,drafting);
check('drafting pattern drawn at 1/4" = 1\'-0" (0.01 ft on paper = 0.48 ft model)',lines(s)===3,String(lines(s)));
check('solid fill and missing patterns draw nothing',hatchSegments(square,{...horizontal,solid:true}).length===0&&hatchSegments(square,null).length===0);
check('over-dense patterns are skipped',hatchSegments(square,{...horizontal,grids:[{...horizontal.grids[0],offsetFeet:.0001/FT}]}).length===0);
// An L shape (concave): lines are clipped to the shape, not its bounding box.
const L=[0,0,1,0,1,.2, 0,0,1,.2,0,.2, 0,.2,.2,.2,.2,1, 0,.2,.2,1,0,1];s=hatchSegments(L,horizontal);
const covered=[...Array(lines(s)).keys()].reduce((t,i)=>t+len(s,i*4),0);
check('concave cut: total line length matches the L shape',Math.abs(covered-(1*3+.2*8))<1e-6,covered.toFixed(4));
process.exit(failed?1:0);

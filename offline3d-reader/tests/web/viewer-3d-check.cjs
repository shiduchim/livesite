// Headless Chromium check of the 3D page (three.html) with a stand-in for the WebView2 host.
// Usage: python3 -I extract-glb.py ../../samples/SYNTHETIC-Offline3D.pdf scene.glb
//        node viewer-3d-check.cjs ../../src/Viewer/web scene.glb <out dir>
// Needs Playwright with Chromium. Uses SwiftShader, so it checks behaviour, not GPU speed.
const {chromium}=require('playwright');const fs=require('fs'),path=require('path');
const [web,glbPath,out]=process.argv.slice(2);const glb=fs.readFileSync(glbPath);
const types={'.html':'text/html','.mjs':'text/javascript','.js':'text/javascript','.css':'text/css','.wasm':'application/wasm'};
const results=[];const check=(name,ok,detail='')=>{results.push({name,ok,detail});console.log((ok?'PASS ':'FAIL ')+name+(detail?' — '+detail:''));};
(async()=>{
 const browser=await chromium.launch({args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--ignore-gpu-blocklist']});
 const page=await browser.newPage({viewport:{width:900,height:700}});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
 await page.route('https://offline3d.example/**',route=>{const file=path.join(web,new URL(route.request().url()).pathname);if(!fs.existsSync(file))return route.fulfill({status:404,body:''});route.fulfill({status:200,body:fs.readFileSync(file),contentType:types[path.extname(file)]||'application/octet-stream'});});
 await page.addInitScript(b64=>{
  const listeners={message:[],sharedbufferreceived:[]};window.__sent=[];
  window.__host=m=>{for(const f of listeners.message)f({data:m});};
  window.__sendBuffer=()=>{const bytes=Uint8Array.from(atob(b64),c=>c.charCodeAt(0));const buffer=bytes.buffer;for(const f of listeners.sharedbufferreceived)f({getBuffer:()=>buffer,additionalData:{type:'sceneBuffer',size:bytes.length}});};
  window.__size=atob(b64).length;
  window.chrome={webview:{postMessage:m=>window.__sent.push(m),addEventListener:(t,f)=>listeners[t].push(f),releaseBuffer:()=>{}}};
  // The panel opens with zero width while the user picks a spot (ClickTo3D).
  document.addEventListener('DOMContentLoaded',()=>{const s=document.createElement('style');s.id='closed';s.textContent='.model-area{display:none!important}';document.head.append(s);});
 },glb.toString('base64'));
 await page.goto('https://offline3d.example/three.html');
 await page.waitForFunction(()=>window.__sent.some(m=>m.type==='ready'));
 check('page reports ready',true);
 // Host: defaults, then the model while the panel is still closed.
 await page.evaluate(()=>{window.__host({type:'boxDefaults',square:3.048,above:3.048,below:.1524,unit:'feet',isDefault:true});window.__host({type:'scene',size:window.__size,identities:[]});window.__sendBuffer();});
 await page.waitForTimeout(800);
 check('model waits while the panel has no size',await page.evaluate(()=>!window.__sent.some(m=>m.type==='loaded')));
 const fields=()=>page.evaluate(()=>['above','below','square'].map(k=>document.getElementById('box-'+k).value).join(' '));
 check('settings show the default box (ClickTo3D layout)',await fields()==='10.00 0.50 10.00',await fields());
 check('Restore default is disabled at the factory size',await page.isDisabled('#restore-box-default'));
 // Open the panel: the box follows the click.
 await page.evaluate(()=>document.getElementById('closed').remove());
 await page.waitForFunction(()=>window.__sent.some(m=>m.type==='loaded'),null,{timeout:60000});
 check('model loads once the panel opens',true);
 const testing=()=>import('./three.mjs').then(m=>m.testing);
 const bounds=await page.evaluate(async()=>{const r=(await import('./three.mjs')).testing.renderer;const {min,max}=r.scene.getWorldExtends(m=>r.models.includes(m));return{min:min.asArray(),max:max.asArray()};});
 const c=bounds.min.map((v,k)=>(v+bounds.max[k])/2),size=bounds.min.map((v,k)=>bounds.max[k]-v);
 await page.evaluate(([c,size])=>window.__host({type:'box',fitBox:true,x:c[0],y:c[1]-size[1]*.3,z:c[2],width:size[0]*.8,depth:size[2]*.8,above:size[1]*.6,below:size[1]*.3,minX:c[0]-size[0]*.4,maxX:c[0]+size[0]*.4,minY:c[1]-size[1]*.6,maxY:c[1]+size[1]*.3,minZ:c[2]-size[2]*.4,maxZ:c[2]+size[2]*.4}),[c,size]);
 await page.waitForTimeout(1200);
 check('loading overlay hidden after load',await page.isHidden('#loading'));
 check('no Babylon.js loading logo',await page.evaluate(()=>!document.getElementById('babylonjsLoadingDiv')));
 await page.screenshot({path:path.join(out,'viewer-3d-babylon-shaded.png')});
 // Cut patterns (only when the model's materials carry them, e.g. add-cut-pattern.py).
 const patterned=glb.includes(Buffer.from('cutPattern'));
 if(patterned){await page.waitForTimeout(800);const hatch=await page.evaluate(async()=>{const r=(await import('./three.mjs')).testing.renderer;return r.scene.meshes.filter(m=>m.name==='Cut pattern').map(m=>m.getTotalVertices());});
  check('cut patterns drawn on section caps',hatch.length>0&&hatch.every(n=>n>=2),JSON.stringify(hatch));await page.screenshot({path:path.join(out,'viewer-3d-cut-patterns.png')});}
 // Grips are smaller than 0.5.3 (39 x 18).
 const grip=await page.evaluate(()=>{const b=[...document.querySelectorAll('.section-grip')].find(b=>!b.hidden);const st=getComputedStyle(b.querySelector('svg'));const g=b.getBoundingClientRect();return{w:parseFloat(st.width),h:parseFloat(st.height),x:g.x+g.width/2,y:g.y+g.height/2};});
 check('resize arrows are smaller',grip.w<=30&&grip.h<=14,`${grip.w}×${grip.h}`);
 // Wheel over a grip still zooms.
 const scale0=await page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.orthoScale);
 await page.mouse.move(grip.x,grip.y);await page.mouse.wheel(0,-120);await page.waitForTimeout(150);
 const scale1=await page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.orthoScale);
 check('wheel over a resize arrow zooms',scale1<scale0*0.9,`${scale0.toFixed(4)} → ${scale1.toFixed(4)}`);
 await page.mouse.wheel(0,120);await page.waitForTimeout(150);
 // Orbit: dragging right turns the model the other way from 0.5.3 (alpha increases).
 const alpha=async()=>page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.camera.alpha);
 const a0=await alpha();await page.mouse.move(150,600);await page.mouse.down();await page.mouse.move(250,600,{steps:5});await page.mouse.up();const a1=await alpha();
 check('left drag to the right increases alpha (reversed from 0.5.3)',a1>a0,`${a0.toFixed(3)} → ${a1.toFixed(3)}`);
 // Shift + middle orbits.
 await page.keyboard.down('Shift');await page.mouse.move(150,600);await page.mouse.down({button:'middle'});await page.mouse.move(100,600,{steps:5});await page.mouse.up({button:'middle'});await page.keyboard.up('Shift');const a2=await alpha();
 check('Shift + middle drag to the left decreases alpha',a2<a1,`${a1.toFixed(3)} → ${a2.toFixed(3)}`);
 await page.click('#reset');await page.waitForTimeout(500);
 // Brightness.
 await page.click('#settings-toggle');
 const exposure=()=>page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.scene.imageProcessingConfiguration.exposure);
 await page.$eval('#brightness',e=>{e.value='1.4';e.dispatchEvent(new Event('input'));});
 check('Brightness raises Babylon exposure',Math.abs(await exposure()-1.4)<1e-6,String(await exposure()));
 await page.click('#brightness-reset');check('Brightness reset returns to 1',Math.abs(await exposure()-1)<1e-6&&await page.inputValue('#brightness')==='1');
 check('Exposure and Environment sliders removed',await page.evaluate(()=>!document.getElementById('exposure')&&!document.getElementById('environment')));
 await page.screenshot({path:path.join(out,'viewer-3d-settings.png')});
 // Section box settings: edits save at once (metres), steppers, validation, units, Reset.
 const sent=type=>page.evaluate(t=>window.__sent.filter(m=>m.type===t),type);
 await page.fill('#box-above','12');await page.press('#box-above','Enter');
 let saved=(await sent('setBoxDefaults')).pop();check('editing a field saves the size in metres',saved&&Math.abs(saved.above-12*.3048)<1e-9&&Math.abs(saved.square-3.048)<1e-9&&Math.abs(saved.below-.1524)<1e-9,JSON.stringify(saved));
 await page.click('#box-square + .steps [data-step="1"]');saved=(await sent('setBoxDefaults')).pop();check('▲ adds exactly 1 ft',await page.inputValue('#box-square')==='11.00'&&Math.abs(saved.square-11*.3048)<1e-9);
 for(let i=0;i<2;i++)await page.click('#box-below + .steps [data-step="-1"]');check('▼ stops height below at 0',await page.inputValue('#box-below')==='0.00');
 const before=(await sent('setBoxDefaults')).length;await page.fill('#box-square','0');await page.press('#box-square','Enter');
 check('invalid size is refused and not saved',(await sent('setBoxDefaults')).length===before&&await page.evaluate(()=>document.getElementById('box-square').classList.contains('invalid')));
 await page.evaluate(()=>window.__host({type:'boxDefaults',square:4,above:3,below:.2,unit:'feet',isDefault:false}));
 check('host values replace the fields and enable Reset',await fields()==='9.84 0.66 13.12'&&!(await page.isDisabled('#restore-box-default')),await fields());
 await page.click('#unit-meters');check('Meters shows metres and is saved',await fields()==='3.00 0.20 4.00'&&(await sent('setUnit')).pop()?.unit==='meters',await fields());
 await page.click('#restore-box-default');check('Reset asks the host for the factory size',(await sent('restoreBoxDefault')).length===1);
 const geo=await page.evaluate(()=>{const r=e=>e.getBoundingClientRect(),sw=r(document.querySelector('.unit-switch')),num=r(document.querySelector('#box-above').parentElement),b=r(document.getElementById('unit-feet'));return{switchRight:sw.right,fieldRight:num.right,switchH:sw.height,buttonH:b.height,fieldW:num.width,fieldH:num.height,gap:r(document.querySelector('.box-grid')).top-sw.bottom};});
 check('Feet/Meters is 19 px high and lines up with the number fields (style guide)',Math.abs(geo.switchRight-geo.fieldRight)<=1&&geo.switchH===19&&geo.buttonH===17&&geo.fieldW===81&&geo.fieldH===23&&Math.abs(geo.gap-16)<=1,JSON.stringify(geo));
 await page.screenshot({path:path.join(out,'viewer-3d-box-settings.png')});
 await page.click('#unit-feet');
 // Navigator toggle.
 await page.uncheck('#navigator');check('turning off the cube hides cube and Home',await page.isHidden('.view-cube')&&await page.isHidden('#reset'));
 check('setting persists',await page.evaluate(()=>JSON.parse(localStorage.getItem('offline3d.view.v1')).navigator===false));
 await page.check('#navigator');
 check('Home button says Home',(await page.textContent('#reset')).trim()==='⌂ Home');
 await page.keyboard.press('Escape');
 // Measure.
 await page.click('#tools-toggle');await page.click('#measure');
 const pts=await page.evaluate(async()=>{const r=(await import('./three.mjs')).testing.renderer,canvas=document.getElementById('scene').getBoundingClientRect(),found=[];for(let y=canvas.top+40;y<canvas.bottom-40&&found.length<2;y+=17)for(let x=canvas.left+40;x<canvas.right-40&&found.length<2;x+=17){const hit=document.elementFromPoint(x,y)?.id==='scene'&&r.pick({clientX:x,clientY:y,buttons:0});if(hit&&(!found.length||Math.hypot(x-found[0][0],y-found[0][1])>150))found.push([x,y]);}return found;});
 check('found two model points to measure',pts.length===2,JSON.stringify(pts));
 if(pts.length===2){await page.mouse.click(pts[0][0],pts[0][1]);await page.mouse.move(pts[1][0],pts[1][1],{steps:4});await page.waitForTimeout(100);check('preview line follows the pointer',await page.evaluate(()=>!!document.querySelector('#measure-layer line.preview')));await page.mouse.click(pts[1][0],pts[1][1]);await page.waitForTimeout(100);
  const label=await page.evaluate(()=>[...document.querySelectorAll('#measure-layer text')].map(t=>t.textContent));check('measurement shows feet-inches',label.length===2&&/^\d+'-\d+/.test(label[0]),JSON.stringify(label));
  await page.screenshot({path:path.join(out,'viewer-3d-measure.png')});
  const snap=await page.evaluate(async(pts)=>{const t=(await import('./three.mjs')).testing,r=t.renderer;return pts.map(([x,y])=>{const h=r.pick({clientX:x,clientY:y,buttons:0});return{snap:t.measure.snap({clientX:x,clientY:y}),picked:h.pickedPoint.asArray(),mesh:h.pickedMesh.name,raw:h.rawMesh?.name,pixel:t.measure.pixel(),a:t.measure.a,b:t.measure.b};});},pts);check('snap returns a point',!!snap[0].snap);}
 await page.keyboard.press('Escape');check('Esc ends measuring',await page.evaluate(()=>!document.body.classList.contains('measuring')&&!document.querySelector('#measure-layer *')));
 // Three.js: Realistic greyed out, engine runs, brightness affects lights.
 await page.click('#settings-toggle');await page.selectOption('#style','realistic');await page.waitForTimeout(300);
 await page.selectOption('#engine','three');await page.waitForFunction(()=>!document.getElementById('engine').disabled,null,{timeout:60000});await page.waitForTimeout(1500);
 check('Realistic greyed out for Three.js',await page.evaluate(()=>document.querySelector('#style option[value=realistic]').disabled));
 check('Realistic falls back to Shaded on Three.js',await page.inputValue('#style')==='shaded');
 const fill=()=>page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.fill.intensity);
 const f0=await fill();await page.$eval('#brightness',e=>{e.value='1.5';e.dispatchEvent(new Event('input'));});const f1=await fill();
 check('Brightness scales Three.js lights',Math.abs(f1/f0-1.5)<1e-6,`${f0} → ${f1}`);
 await page.click('#brightness-reset');
 const t0=await page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.cameraState.alpha);await page.keyboard.press('Escape');
 await page.mouse.move(150,600);await page.mouse.down();await page.mouse.move(250,600,{steps:5});await page.mouse.up();
 const t1=await page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.cameraState.alpha);check('Three.js orbit matches Babylon direction',t1>t0,`${t0.toFixed(3)} → ${t1.toFixed(3)}`);
 const r0=await page.evaluate(async()=>(await import('./three.mjs')).testing.renderer.cameraState.radius);await page.mouse.move(450,350);await page.mouse.down({button:'middle'});await page.mouse.up({button:'middle'});await page.mouse.down({button:'middle'});await page.mouse.up({button:'middle'});await page.waitForTimeout(1500);
 const fitted=await page.evaluate(async()=>{const r=(await import('./three.mjs')).testing.renderer;const f=r.fitState(r.bounds);return Math.abs(f.radius-r.cameraState.radius)/f.radius;});check('Three.js double-click on the wheel fits the box',fitted<1e-2,String(fitted));
 await page.screenshot({path:path.join(out,'viewer-3d-three.png')});
 await page.click('#settings-toggle');await page.selectOption('#engine','babylon');await page.waitForFunction(()=>!document.getElementById('engine').disabled,null,{timeout:60000});
 check('Realistic enabled again on Babylon.js',await page.evaluate(()=>!document.querySelector('#style option[value=realistic]').disabled));
 // Reload keeps settings.
 await page.$eval('#quality',e=>{e.value='high';e.dispatchEvent(new Event('change'));});
 check('view settings saved',await page.evaluate(()=>JSON.parse(localStorage.getItem('offline3d.view.v1')).quality==='high'));
 // Reopen: the panel is zero wide when the host asks to fit the new box.
 await page.keyboard.press('Escape');await page.addStyleTag({content:'.model-area.closed{display:none!important}'});await page.evaluate(()=>document.querySelector('.model-area').classList.add('closed'));
 await page.evaluate(([c,size])=>window.__host({type:'box',fitBox:true,x:c[0],y:c[1],z:c[2],width:size[0]*.5,depth:size[2]*.5,above:size[1]*.5,below:size[1]*.2,minX:c[0]-size[0]*.25,maxX:c[0]+size[0]*.25,minY:c[1]-size[1]*.2,maxY:c[1]+size[1]*.5,minZ:c[2]-size[2]*.25,maxZ:c[2]+size[2]*.25}),[c,size]);
 await page.waitForTimeout(200);await page.evaluate(()=>document.querySelector('.model-area').classList.remove('closed'));await page.waitForTimeout(600);
 const reopenFit=await page.evaluate(async()=>{const r=(await import('./three.mjs')).testing.renderer;return Math.abs(r.fitState(r.bounds).radius*.45-r.orthoScale)/r.orthoScale;});
 check('reopened panel fits the new box at its real size',reopenFit<1e-3,String(reopenFit));
 check('no page errors',errors.length===0,errors.join(' | '));
 fs.writeFileSync(path.join(out,'viewer-3d-results.json'),JSON.stringify(results,null,1));
 await browser.close();process.exit(results.every(r=>r.ok)?0:1);
})().catch(e=>{console.error(e);process.exit(2);});

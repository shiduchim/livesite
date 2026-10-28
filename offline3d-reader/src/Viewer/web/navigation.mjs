// Logarithmic cursor zoom: about 1.94x per 120-pixel wheel notch.
export function wheelFactor(event,element){const pixels=event.deltaY*(event.deltaMode===1?16:event.deltaMode===2?element.clientHeight:1);return Math.exp(Math.max(-240,Math.min(240,pixels))*.0055);}
export function navigation(renderer){
 renderer.animateTo=function(end,duration=300){const start=this.getCameraState?.();if(!start)return;cancelAnimationFrame(this.animation);const began=performance.now(),delta=((end.alpha-start.alpha+Math.PI)%(2*Math.PI)+2*Math.PI)%(2*Math.PI)-Math.PI;const frame=()=>{const raw=Math.min(1,(performance.now()-began)/duration),t=raw*raw*(3-2*raw);this.setCameraState({alpha:start.alpha+delta*t,beta:start.beta+(end.beta-start.beta)*t,radius:Math.exp(Math.log(Math.max(1e-6,start.radius))*(1-t)+Math.log(Math.max(1e-6,end.radius))*t),target:start.target.map((v,k)=>v+(end.target[k]-v)*t)});if(raw<1)this.animation=requestAnimationFrame(frame);};frame();};
 renderer.home=function(){const start=this.getCameraState?.();this.reset();const end=this.getCameraState?.();if(start&&end){this.setCameraState(start);this.animateTo(end);}};
 renderer.focus=function(name){const bounds=this.modelBounds?.(name),state=this.getCameraState?.();if(!bounds||!state)return;const fitted=this.fitState?.(bounds,state.alpha,state.beta,1.6);if(fitted){this.animateTo(fitted);return;}state.target=bounds.min.map((v,k)=>(v+bounds.max[k])/2);state.radius=Math.max(.001,...bounds.max.map((v,k)=>v-bounds.min[k]))*1.7;this.animateTo(state);};
 // Zoom to fit the section box, keeping the orientation (Revit: double-click the wheel).
 renderer.fit=function(){const state=this.fitState?.(this.bounds);if(state)this.animateTo(state);};
 return renderer;
}

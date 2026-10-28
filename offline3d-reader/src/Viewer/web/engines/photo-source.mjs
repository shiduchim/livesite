import * as THREE from 'three';
import {WebGLPathTracer,GradientEquirectTexture} from 'three-gpu-pathtracer';
import {ThreeAdapter} from './three-source.mjs';
export class PhotoAdapter extends ThreeAdapter{
 constructor(){super();this.name='Three.js Photo';}
 async createEngine(){await super.createEngine();this.photoEnvironment=new GradientEquirectTexture(256);this.photoEnvironment.topColor.set(0xffffff);this.photoEnvironment.bottomColor.set(0x969696);this.photoEnvironment.update();this.scene.environment=this.photoEnvironment;this.scene.background=new THREE.Color(100,100,100);this.tracer=new WebGLPathTracer(this.renderer);this.tracer.renderDelay=0;this.tracer.minSamples=1;this.tracer.fadeDuration=0;this.tracer.dynamicLowRes=false;this.tracer.bounces=8;this.tracer.filterGlossyFactor=.5;this.photoReady=false;}
 async replaceModel(bytes){await super.replaceModel(bytes);this.tracer.setScene(this.scene,this.camera);this.photoReady=true;this.lastCamera=null;}
 applySettings(){super.applySettings();if(this.tracer){this.tracer.bounces=this.options.quality==='ultra'?8:4;this.tracer.reset();}}
 shouldContinue(){return this.photoReady&&this.tracer.samples<(this.photoSampleTargetOverride??({low:8,medium:32,high:128,ultra:512}[this.options.quality]||128));}
 renderFrame(w,h){super.renderFrame(w,h);if(!this.photoReady)return;const key=JSON.stringify([this.cameraState,w,h]);if(key!==this.lastCamera){this.tracer.updateCamera();this.lastCamera=key;this.lastMovement=performance.now();}this.tracer.pausePathTracing=performance.now()<(this.lastMovement||0)+350||performance.now()<this.wakeUntil;this.tracer.renderSample();this.extraStatus=`${Math.floor(this.tracer.samples)} path-traced samples`;}
 destroyEngine(){this.photoReady=false;this.tracer?.dispose();this.tracer=null;this.photoEnvironment?.dispose();this.photoEnvironment=null;super.destroyEngine();}
}

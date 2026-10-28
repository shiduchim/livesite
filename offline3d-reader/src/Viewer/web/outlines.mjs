// Revit-style outlines and anti-aliasing for the Babylon view.
// The model's own feature edges are drawn as lines. This pass adds what lines
// cannot know in advance: silhouettes of curved and cut faces against whatever
// lies behind them. It reads view distance and normals from a geometry buffer.
const fragment=`precision highp float;
varying vec2 vUV;
uniform sampler2D textureSampler;
#ifdef OUTLINES
uniform sampler2D depthSampler;
uniform sampler2D normalSampler;
uniform float tolerance;
uniform float depthSign;
uniform vec4 edgeColor;
ivec2 size;
float distanceAt(ivec2 p){float d=depthSign*texelFetch(depthSampler,clamp(p,ivec2(0),size-1),0).r;return d>0.?d:1e9;}
vec3 normalAt(ivec2 p){return texelFetch(normalSampler,clamp(p,ivec2(0),size-1),0).xyz;}
float fold(vec3 n,float c,ivec2 q){float d=distanceAt(q);return d>c?1.-smoothstep(.3,.55,dot(n,normalAt(q))):0.;}
#endif
void main(void){
 vec4 color=texture2D(textureSampler,vUV);
#ifdef OUTLINES
 size=textureSize(depthSampler,0);
 ivec2 p=ivec2(vUV*vec2(size));
 float c=distanceAt(p);
 if(c<1e8){
  float l=distanceAt(p-ivec2(1,0)),r=distanceAt(p+ivec2(1,0)),b=distanceAt(p-ivec2(0,1)),t=distanceAt(p+ivec2(0,1));
  float tol=max(tolerance,c*4e-6);
  // A plane has no second difference in an orthographic view. A positive one
  // marks the near side of a step, so every silhouette is one pixel wide.
  float edge=smoothstep(tol,2.*tol,max(l+r-2.*c,b+t-2.*c));
  // A curved surface seen edge-on against a farther face (a pipe on a floor).
  vec3 n=normalAt(p);
  if(abs(n.z)<.3)edge=max(edge,max(max(fold(n,c,p-ivec2(1,0)),fold(n,c,p+ivec2(1,0))),max(fold(n,c,p-ivec2(0,1)),fold(n,c,p+ivec2(0,1)))));
  color.rgb=mix(color.rgb,edgeColor.rgb,edge*edgeColor.a);
 }
#endif
 gl_FragColor=color;
}`;
export class ScreenOutlines {
 constructor(scene,camera){
  const B=window.BABYLON,engine=scene.getEngine();this.scene=scene;this.camera=camera;this.meshes=[];this.color=new B.Color4(.1,.1,.1,.9);this.tolerance=.01;
  B.Effect.ShadersStore.revitOutlineFragmentShader=fragment;
  const caps=engine.getCaps();this.supported=!engine.isWebGPU&&engine.webGLVersion>1&&!!(caps.textureFloatRender||caps.textureHalfFloatRender);
  this.post=new B.PostProcess('Outlines','revitOutline',['tolerance','depthSign','edgeColor'],['depthSampler','normalSampler'],1,null,B.Texture.NEAREST_SAMPLINGMODE,engine,false,'');
  this.fxaa=new B.FxaaPostProcess('FXAA',1,null,B.Texture.BILINEAR_SAMPLINGMODE,engine);
  // The outline pass comes first so the scene renders into its multisampled
  // input; FXAA then smooths the outlines with everything else.
  camera.attachPostProcess(this.post,0);camera.attachPostProcess(this.fxaa,1);
  this.post.onApply=effect=>{if(!this.enabled)return;const gbr=this.scene.geometryBufferRenderer,textures=gbr.getGBuffer().textures;effect.setTexture('depthSampler',textures[gbr.getTextureIndex(B.GeometryBufferRenderer.DEPTH_TEXTURE_TYPE)]);effect.setTexture('normalSampler',textures[gbr.getTextureIndex(B.GeometryBufferRenderer.NORMAL_TEXTURE_TYPE)]);effect.setFloat('tolerance',this.tolerance);effect.setFloat('depthSign',this.scene.useRightHandedSystem?-1:1);effect.setDirectColor4('edgeColor',this.color);};
 }
 geometryBuffer(){
  // One shared buffer (contact shading reuses it). Distance needs full float
  // precision; normals are fine in half floats.
  const B=window.BABYLON,scene=this.scene;if(scene.geometryBufferRenderer)return scene.geometryBufferRenderer;
  const caps=scene.getEngine().getCaps(),full=caps.textureFloatRender?B.Constants.TEXTURETYPE_FLOAT:B.Constants.TEXTURETYPE_HALF_FLOAT,half=caps.textureHalfFloatRender?B.Constants.TEXTURETYPE_HALF_FLOAT:full;
  const gbr=scene.enableGeometryBufferRenderer(1,B.Constants.TEXTUREFORMAT_DEPTH24_STENCIL8,{[B.GeometryBufferRenderer.DEPTH_TEXTURE_TYPE]:{textureType:full,textureFormat:B.Constants.TEXTUREFORMAT_RED},[B.GeometryBufferRenderer.NORMAL_TEXTURE_TYPE]:{textureType:half,textureFormat:B.Constants.TEXTUREFORMAT_RGBA}});
  if(!gbr)return null;gbr.renderTransparentMeshes=false;gbr.renderList=this.meshes;return gbr;
 }
 setMeshes(meshes){this.meshes=meshes;const gbr=this.scene.geometryBufferRenderer;if(gbr)gbr.renderList=meshes;}
 configure({enabled,samples}){
  const want=this.supported&&enabled;if(want&&!this.geometryBuffer())this.supported=false;const on=this.supported&&enabled;
  if(on!==this.enabled){this.enabled=on;this.post.updateEffect(on?'#define OUTLINES':'');}
  if(this.post.samples!==samples)this.post.samples=samples;
 }
 // Silhouettes need a step deeper than about two pixels of the current view.
 update(pixelSize){this.tolerance=Math.max(1e-5,pixelSize*2);}
 // The scene owns and disposes the passes and the geometry buffer.
 dispose(){this.post=null;this.fxaa=null;this.meshes=[];}
}

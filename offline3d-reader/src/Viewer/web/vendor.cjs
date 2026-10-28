const fs=require('fs');fs.mkdirSync('vendor',{recursive:true});
for(const [a,b] of [['babylonjs/babylon.js','babylon.js'],['babylonjs-loaders/babylonjs.loaders.min.js','loaders.js'],['pdfjs-dist/build/pdf.mjs','pdf.mjs'],['pdfjs-dist/build/pdf.worker.mjs','pdf.worker.mjs']])fs.copyFileSync('node_modules/'+a,'vendor/'+b);
for(const name of ['babylonjs','babylonjs-loaders','pdfjs-dist']){const file=fs.readdirSync('node_modules/'+name).find(f=>/^licen[sc]e/i.test(f));if(file)fs.copyFileSync('node_modules/'+name+'/'+file,'vendor/'+name+'-LICENSE');}

const esbuild=require("esbuild");
for(const name of ["three","playcanvas","photo"])esbuild.buildSync({entryPoints:["engines/"+name+"-source.mjs"],bundle:true,format:"esm",minify:true,external:["../comparison-base.mjs"],outfile:"vendor/"+name+"-adapter.mjs"});

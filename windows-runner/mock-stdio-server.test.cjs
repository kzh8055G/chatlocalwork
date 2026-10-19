'use strict';
const readline = require('node:readline');
readline.createInterface({input:process.stdin}).on('line',line=>{
  let request;
  try{request=JSON.parse(line)}catch{return;}
  if(request.id===undefined)return;
  const content={jsonrpc:'2.0',id:request.id};
  switch(request.method){
    case 'initialize':content.result={protocolVersion:'2025-06-18',capabilities:{tools:{}},serverInfo:{name:'stdio-mock',version:'1.0'}};break;
    case 'tools/list':content.result={tools:[{name:'ping',description:'Mock ping',inputSchema:{type:'object',properties:{word:{type:'string'}}}},{name:'dangerous',inputSchema:{type:'object'}}]};break;
    case 'tools/call':content.result={content:[{type:'text',text:`${request.params.name}:${request.params.arguments.word??''}:${process.pid}`}],isError:false};break;
    default:content.error={code:-32601,message:'Not implemented'};
  }
  process.stdout.write(JSON.stringify(content)+'\n');
});

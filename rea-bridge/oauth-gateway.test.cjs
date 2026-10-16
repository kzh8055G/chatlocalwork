'use strict';
const {test}=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs');const os=require('node:os');const path=require('node:path');const crypto=require('node:crypto');const http=require('node:http');const {spawn}=require('node:child_process');
test('OAuth PKCE, audience isolation, protected proxy, revocation and denial',async()=>{
 const tmp=fs.mkdtempSync(path.join(os.tmpdir(),'rea-oauth-test-'));const approval=crypto.randomBytes(32).toString('hex'),upstreamKey=crypto.randomBytes(32).toString('hex');
 fs.writeFileSync(path.join(tmp,'approval'),approval);fs.writeFileSync(path.join(tmp,'upstream'),upstreamKey);
 const upstream=http.createServer((req,res)=>{assert.equal(req.headers.authorization,'Bearer '+upstreamKey);res.writeHead(200,{'content-type':'application/json'});res.end(JSON.stringify({upstream:true}));});
 await new Promise(r=>upstream.listen(0,'127.0.0.1',r));const backendPort=upstream.address().port;const gatewayPort=backendPort+1;
 const child=spawn(process.execPath,[path.join(__dirname,'oauth-gateway.cjs')],{env:{...process.env,REA_OAUTH_PORT:String(gatewayPort),REA_PUBLIC_ORIGIN:'https://example.test',REA_BRIDGE_API_KEY_FILE:path.join(tmp,'upstream'),REA_OAUTH_APPROVAL_KEY_FILE:path.join(tmp,'approval'),REA_UPSTREAM_URL:'http://127.0.0.1:'+backendPort+'/mcp'}});
 const base='http://127.0.0.1:'+gatewayPort;
 try{
  let ok=false;for(let i=0;i<30;i++){try{const r=await fetch(base+'/rea/mcp');if(r.status===401){ok=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ok,'gateway ready');
  const post=async(p,obj)=>fetch(base+p,{method:'POST',headers:{'content-type':'application/x-www-form-urlencoded'},body:new URLSearchParams(obj),redirect:'manual'});
  assert.equal((await fetch(base+'/.well-known/oauth-protected-resource/rea/mcp')).status,200);
  const registered=await fetch(base+'/rea/register',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({client_name:'test',redirect_uris:['https://client.example/cb'],token_endpoint_auth_method:'none'})});assert.equal(registered.status,201);const {client_id}=await registered.json();
  const verifier=crypto.randomBytes(32).toString('base64url'),challenge=crypto.createHash('sha256').update(verifier).digest('base64url');
  const auth=new URL(base+'/rea/authorize');Object.entries({client_id,redirect_uri:'https://client.example/cb',response_type:'code',code_challenge:challenge,code_challenge_method:'S256',resource:'https://example.test/rea/mcp',state:'state123'}).forEach(([k,v])=>auth.searchParams.set(k,v));
  assert.equal((await fetch(auth)).status,200);const html=await(await fetch(auth)).text();const nonce=html.match(/name="request" value="([^"]+)"/)[1];
  assert.equal((await post('/rea/authorize',{request:nonce,key:'wrong'})).status,403);
  const approved=await post('/rea/authorize',{request:nonce,key:approval});assert.equal(approved.status,303);const code=new URL(approved.headers.get('location')).searchParams.get('code');
  const wrongAudience=await post('/rea/token',{grant_type:'authorization_code',client_id,code,code_verifier:verifier,redirect_uri:'https://client.example/cb',resource:'https://example.test/mcp'});assert.equal(wrongAudience.status,400);
  const wrongVerifier=await post('/rea/token',{grant_type:'authorization_code',client_id,code,code_verifier:'bad',redirect_uri:'https://client.example/cb',resource:'https://example.test/rea/mcp'});assert.equal(wrongVerifier.status,400);
  const tokenResponse=await post('/rea/token',{grant_type:'authorization_code',client_id,code,code_verifier:verifier,redirect_uri:'https://client.example/cb',resource:'https://example.test/rea/mcp'});assert.equal(tokenResponse.status,200);const token=await tokenResponse.json();
  assert.equal((await post('/rea/token',{grant_type:'authorization_code',client_id,code,code_verifier:verifier,redirect_uri:'https://client.example/cb',resource:'https://example.test/rea/mcp'})).status,400);
  assert.equal((await fetch(base+'/rea/mcp',{method:'POST',headers:{authorization:'Bearer '+token.access_token},body:'{}'})).status,200);
  const refreshed=await post('/rea/token',{grant_type:'refresh_token',client_id,refresh_token:token.refresh_token,resource:'https://example.test/rea/mcp'});assert.equal(refreshed.status,200);assert.equal((await post('/rea/token',{grant_type:'refresh_token',client_id,refresh_token:token.refresh_token,resource:'https://example.test/rea/mcp'})).status,400);
  assert.equal((await fetch(base+'/rea/mcp',{method:'POST',body:'{}'})).status,401);
  console.log('OAuth gateway: PKCE, resource, one-time code, refresh rotation, proxy and 401 passed');
 }finally{child.kill();await new Promise(r=>upstream.close(r));fs.rmSync(tmp,{recursive:true,force:true});}
});

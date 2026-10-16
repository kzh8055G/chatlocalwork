'use strict';
// Standalone OAuth-protected Streamable HTTP gateway. This file is intentionally
// loopback-only until the public HTTPS routing and OAuth flow have been tested.
const http = require('node:http');
const fs = require('node:fs');
const crypto = require('node:crypto');
const { URL } = require('node:url');
const { Readable } = require('node:stream');

const base = '/rea';
const publicOrigin = process.env.REA_PUBLIC_ORIGIN;
if (!publicOrigin || !/^https:\/\/[^/]+$/.test(publicOrigin)) throw Error('REA_PUBLIC_ORIGIN must be an HTTPS origin');
const issuer = publicOrigin + base;
const resource = issuer + '/mcp';
const port = Number(process.env.REA_OAUTH_PORT || 4189);
const upstream = new URL(process.env.REA_UPSTREAM_URL || 'http://127.0.0.1:4188/mcp');
if (upstream.hostname !== '127.0.0.1' || upstream.protocol !== 'http:') throw Error('upstream must use loopback HTTP');
const approvalKeyFile = process.env.REA_OAUTH_APPROVAL_KEY_FILE;
const upstreamKeyFile = process.env.REA_BRIDGE_API_KEY_FILE;
if (!approvalKeyFile || !upstreamKeyFile) throw Error('Separate approval and bridge key files are required');
const readKey = file => fs.readFileSync(file, 'utf8').trim();
if (readKey(approvalKeyFile).length < 32 || readKey(upstreamKeyFile).length < 32) throw Error('Weak or missing keys');
if (readKey(approvalKeyFile) === readKey(upstreamKeyFile)) throw Error('Approval and upstream keys must differ');
const clients = new Map();
const grants = new Map();
const codes = new Map();
const tokens = new Map();
const refresh = new Map();
const now = () => Date.now();
const rand = () => crypto.randomBytes(32).toString('base64url');
const hash = v => crypto.createHash('sha256').update(v).digest('base64url');
const eq = (a,b) => { const x=Buffer.from(a),y=Buffer.from(b); return x.length===y.length && crypto.timingSafeEqual(x,y); };
function json(res, code, data, headers={}) { res.writeHead(code, {'content-type':'application/json','cache-control':'no-store',...headers}); res.end(JSON.stringify(data)); }
function err(res, code, name, desc) { json(res,code,{error:name,error_description:desc}); }
function redirectUriOk(uri) { try { const u = new URL(uri); return !u.hash && !u.username && !u.password && (u.protocol==='https:' || (u.protocol==='http:' && ['localhost','127.0.0.1','[::1]'].includes(u.hostname))); } catch { return false; } }
function readBody(req, limit=65536) { return new Promise((resolve,reject)=> {let s='';req.on('data',c=>{s+=c;if(s.length>limit){reject(Error('body too large'));req.destroy();}});req.on('end',()=>resolve(s));req.on('error',reject);}); }
const parse = async req => { const body=await readBody(req); return req.headers['content-type']?.includes('application/json')?JSON.parse(body):Object.fromEntries(new URLSearchParams(body)); };
const oauthHeaders = { 'access-control-allow-origin': '*' };
const protectedMeta = {resource,authorization_servers:[issuer],bearer_methods_supported:['header'],scopes_supported:['rea:read']};
const authMeta = {issuer,authorization_endpoint:issuer+'/authorize',token_endpoint:issuer+'/token',registration_endpoint:issuer+'/register',revocation_endpoint:issuer+'/revoke',response_types_supported:['code'],grant_types_supported:['authorization_code','refresh_token'],code_challenge_methods_supported:['S256'],token_endpoint_auth_methods_supported:['none'],scopes_supported:['rea:read']};
function cleanup(){for(const map of [grants,codes,tokens,refresh]) for(const [k,v] of map) if(v.exp < now()) map.delete(k);}
function validClient(id, uri) {let c=clients.get(id);return c && c.redirect_uris.includes(uri);}
function tokenResult(clientId){const access=rand(),rot=rand(),exp=now()+3600000; tokens.set(hash(access),{clientId,exp});refresh.set(hash(rot),{clientId,exp:now()+86400000});return {access_token:access,refresh_token:rot,token_type:'Bearer',expires_in:3600,scope:'rea:read'};}
const server=http.createServer(async(req,res)=>{
 try {
 const u=new URL(req.url,'http://127.0.0.1'); const p=u.pathname;
 res.setHeader('X-Content-Type-Options','nosniff');
 res.setHeader('Referrer-Policy','no-referrer');
 if(req.method==='GET' && (p==='/.well-known/oauth-protected-resource/rea/mcp' || p==='/.well-known/oauth-protected-resource')) return json(res,200,protectedMeta,oauthHeaders);
 if(req.method==='GET' && (p==='/.well-known/oauth-authorization-server/rea'||p==='/.well-known/oauth-authorization-server')) return json(res,200,authMeta,oauthHeaders);
 if(req.method==='POST' && p===base+'/register') {
  const c=await parse(req); const uris=c.redirect_uris;
  if(!Array.isArray(uris)||!uris.length||!uris.every(redirectUriOk)||c.token_endpoint_auth_method && c.token_endpoint_auth_method!=='none')return err(res,400,'invalid_client_metadata','Invalid redirect URI or authentication method');
  const id=rand();const saved={client_id:id,client_name:String(c.client_name||'REA client').slice(0,100),redirect_uris:uris,token_endpoint_auth_method:'none',grant_types:['authorization_code','refresh_token'],response_types:['code']}; clients.set(id,saved);return json(res,201,saved);
 }
 if(req.method==='GET' && p===base+'/authorize') {
  const {client_id,redirect_uri,code_challenge,code_challenge_method,response_type,state,scope='rea:read',resource:asked}=Object.fromEntries(u.searchParams);
  if(!validClient(client_id,redirect_uri)||response_type!=='code'||code_challenge_method!=='S256'||!(/^[A-Za-z0-9_-]{43}$/.test(code_challenge||''))||scope!=='rea:read'||asked!==resource)return err(res,400,'invalid_request','Invalid OAuth authorization request');
  const nonce=rand();grants.set(nonce,{client_id,redirect_uri,code_challenge,state,exp:now()+300000});
  res.writeHead(200,{'content-type':'text/html; charset=utf-8','cache-control':'no-store','content-security-policy':"default-src 'none'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"});
  return res.end(`<!doctype html><html lang="ko"><meta charset="utf-8"><title>REA 연결 승인</title><body><h2>REA 연결 승인</h2><p>읽기 중심 바이너리 분석 도구 20개에 대한 접근을 승인합니다.</p><form action="${base}/authorize" method="POST"><input type="hidden" name="request" value="${nonce}"><label>승인 키 <input type="password" name="key" required></label><button type="submit">승인</button></form></body></html>`);
 }
 if(req.method==='POST' && p===base+'/authorize') {
  const b=await parse(req);const a=grants.get(b.request);if(!a||a.exp<now())return err(res,400,'invalid_request','Expired request');
  if(!eq(String(b.key||''),readKey(approvalKeyFile)))return err(res,403,'access_denied','Approval key incorrect');
  grants.delete(b.request);const code=rand();codes.set(hash(code),{...a,exp:now()+120000}); const target=new URL(a.redirect_uri);target.searchParams.set('code',code);if(a.state)target.searchParams.set('state',a.state);res.writeHead(303,{location:target.href,'cache-control':'no-store'});return res.end();
 }
 if(req.method==='POST' && p===base+'/token'){
  const b=await parse(req);if(b.resource!==resource)return err(res,400,'invalid_target','Wrong resource');
  if(b.grant_type==='authorization_code'){
   const entry=codes.get(hash(String(b.code||'')));if(!entry||entry.exp<now()||entry.client_id!==b.client_id||entry.redirect_uri!==b.redirect_uri)return err(res,400,'invalid_grant','Invalid code');
   if(!b.code_verifier||hash(b.code_verifier)!==entry.code_challenge)return err(res,400,'invalid_grant','PKCE failed');
   codes.delete(hash(b.code));return json(res,200,tokenResult(b.client_id));
  }
  if(b.grant_type==='refresh_token'){
   const key=hash(String(b.refresh_token||''));const entry=refresh.get(key);if(!entry||entry.exp<now()||entry.clientId!==b.client_id)return err(res,400,'invalid_grant','Invalid refresh token');refresh.delete(key);return json(res,200,tokenResult(entry.clientId));
  }
  return err(res,400,'unsupported_grant_type','Unsupported');
 }
 if(req.method==='POST' && p===base+'/revoke') {const b=await parse(req);const k=hash(String(b.token||''));const item=refresh.get(k)||tokens.get(k);if(item && item.clientId===b.client_id){refresh.delete(k);tokens.delete(k);}return json(res,200,{});}
 if(p===base+'/mcp'){
  const authorization=/^Bearer (\S+)$/i.exec(req.headers.authorization||'');const token=authorization&&tokens.get(hash(authorization[1]));
  if(!token||token.exp<now())return json(res,401,{error:'invalid_token'}, {'www-authenticate':`Bearer resource_metadata="${publicOrigin}/.well-known/oauth-protected-resource/rea/mcp"`});
  if(!['POST','GET','DELETE'].includes(req.method))return err(res,405,'invalid_request','Method not allowed');
  const targetHeaders={...req.headers,host:upstream.host,authorization:'Bearer '+readKey(upstreamKeyFile)};
  delete targetHeaders['content-length'];delete targetHeaders['connection'];
  const prox=http.request(upstream,{method:req.method,headers:targetHeaders},up=>{res.writeHead(up.statusCode||502,Object.fromEntries(Object.entries(up.headers).filter(([k])=>!['transfer-encoding','connection','content-length'].includes(k))));up.pipe(res);});
  prox.on('error',()=>{if(!res.headersSent)err(res,502,'upstream_error','REA upstream unavailable');});req.pipe(prox);return;
 }
 return json(res,404,{error:'not_found'});
 }catch(e){if(!res.headersSent)err(res,400,'invalid_request','Invalid request');}
});
setInterval(cleanup,60000).unref();
server.listen(port,'127.0.0.1',()=>console.log('REA OAuth gateway listening on 127.0.0.1:'+port));
module.exports={server};

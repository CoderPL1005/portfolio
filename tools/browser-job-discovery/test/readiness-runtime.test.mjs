import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,readdirSync,rmSync} from 'node:fs';
import {join} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {execFileSync} from 'node:child_process';
import {readinessFixture,key,token,current,pins,base} from './fixtures/readiness-recovery.mjs';

const root=fileURLToPath(new URL('../',import.meta.url));
for(const defect of [undefined,'binding','tamper','later-provider','missing-cipher','wrong-action']){
  test(`actual verify-only unseal: ${defect??'READY'}; no writes, real sockets or provider`,()=>{
    const f=readinessFixture(defect);mkdirSync(join(root,'.debug'),{recursive:true});
    const local=mkdtempSync(join(root,'.debug','verify-readiness-'));
    try{
      const output=join(local,'state.json'),routes=join(local,'routes.json'),guard=join(local,'guard.mjs'),outputs=join(local,'outputs.txt');
      if(defect!=='missing-cipher')writeFileSync(output+'.enc',f.ciphertexts.syncFinal);
      // Plaintext must never be read, overwritten or used as a legacy fallback.
      writeFileSync(output,'SYNTHETIC PLAINTEXT SENTINEL');writeFileSync(outputs,'UNMODIFIED OUTPUTS');writeFileSync(routes,JSON.stringify(f.routes));
      writeFileSync(guard,`import net from 'node:net';import fs from 'node:fs';import{syncBuiltinESMExports}from'node:module';
net.Socket.prototype.connect=()=>{throw Error('OFFLINE_SOCKET_FORBIDDEN')};
const routes=JSON.parse(fs.readFileSync(${JSON.stringify(routes)},'utf8'));
const deny=()=>{throw Error('OFFLINE_MUTATION_FORBIDDEN')};
for(const n of ['writeFile','appendFile','mkdir','rename','unlink','rm','open'])fs.promises[n]=deny;
syncBuiltinESMExports();
globalThis.fetch=async(url,options)=>{if(options.method!=='GET'||!String(url).startsWith(${JSON.stringify(base)})||options.redirect!=='manual')throw Error('OFFLINE_ENDPOINT_FORBIDDEN');
const value=routes[String(url)];if(!value)throw Error('OFFLINE_ROUTE_MISSING');return value.archive?new Response(Buffer.from(value.archive,'base64')):Response.json(value);};`);
      const before=new Map(readdirSync(local).map(n=>[n,readFileSync(join(local,n))]));
      const stdout=execFileSync(process.execPath,['--import',pathToFileURL(guard).href,'src/cli.mjs','--orchestrate',defect==='wrong-action'?'init':'unseal'],{
        cwd:root,encoding:'utf8',stdio:'pipe',env:{...process.env,DISCOVERY_STATE_FILE:output,DISCOVERY_VERIFY_ONLY:'true',DISCOVERY_CHECKPOINT_KEY:key,
          GH_READ_TOKEN:token,GITHUB_RUN_ID:current,ARTIFACT_RECEIPT:String(pins.syncFinal),GITHUB_OUTPUT:outputs,DISCOVERY_FRESH_SESSION_ID:'',
          DISCOVERY_RECOVER_ACKNOWLEDGED:'false',INITIALIZE_STATE:'true',DISCOVERY_SYNC_ONLY:'true'}});
      const report=JSON.parse(stdout);
      assert.equal(report.status,defect?'BLOCKED':'READY');assert.equal(report.providerRequests,0);
      assert.equal(report.selectedArtifactId,defect?null:pins.originalFinal);
      if(defect==='binding')assert.equal(report.reason,'CONTENT_BINDING_UNVERIFIED');
      if(defect==='tamper')assert.equal(report.reason,'CONTENT_INTEGRITY_VIOLATION');
      if(defect==='later-provider')assert.equal(report.reason,'LATER_HISTORY_NOT_VERIFIED');
      if(defect==='wrong-action')assert.equal(report.reason,'VERIFY_ONLY_UNSEAL_REQUIRED');
      assert.deepEqual(Object.keys(report).sort(),['providerRequests','reason','selectedArtifactId','status']);
      assert.doesNotMatch(stdout,/SYNTHETIC PRIVATE|SYNTHETIC_READ_TOKEN|SENTINEL|cursor|stateFingerprint|encryptedCheckpoint/u);
      assert.ok(!stdout.includes(key));assert.deepEqual(readdirSync(local),[...before.keys()]);
      for(const [name,bytes] of before)assert.deepEqual(readFileSync(join(local,name)),bytes);
    }finally{rmSync(local,{recursive:true,force:true});}
  });
}

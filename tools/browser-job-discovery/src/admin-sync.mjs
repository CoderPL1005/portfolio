import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto';
import { Stop } from './core.mjs';

const MAX_CHECKPOINT = 12 * 1024 * 1024;
const AAD = Buffer.from('portfolio-discovery-checkpoint-v1');
function key(value) {
  if (typeof value !== 'string' || !/^[a-f0-9]{64}$/iu.test(value)) throw new Stop('PRIVATE_CHECKPOINT_KEY_REQUIRED');
  return Buffer.from(value, 'hex');
}
// Only authenticated ciphertext may be uploaded to artifacts on a public repo.
export function encryptCheckpoint(bytes, secret) {
  if (!Buffer.isBuffer(bytes) || bytes.length > MAX_CHECKPOINT) throw new Stop('CHECKPOINT_CAPACITY_REACHED');
  const nonce = randomBytes(12), cipher = createCipheriv('aes-256-gcm', key(secret), nonce);
  cipher.setAAD(AAD);
  const data = Buffer.concat([cipher.update(bytes), cipher.final()]);
  return Buffer.concat([Buffer.from('DSC1'), nonce, cipher.getAuthTag(), data]);
}
export function decryptCheckpoint(bytes, secret) {
  if (!Buffer.isBuffer(bytes) || bytes.length < 32 || bytes.length > MAX_CHECKPOINT + 32 || bytes.subarray(0, 4).toString() !== 'DSC1')
    throw new Stop('PRIVATE_CHECKPOINT_INVALID');
  try {
    const decipher = createDecipheriv('aes-256-gcm', key(secret), bytes.subarray(4, 16));
    decipher.setAAD(AAD); decipher.setAuthTag(bytes.subarray(16, 32));
    return Buffer.concat([decipher.update(bytes.subarray(32)), decipher.final()]);
  } catch { throw new Stop('PRIVATE_CHECKPOINT_INVALID'); }
}
export function syncConfiguration(env = process.env) {
  let origin;
  try { origin = new URL(env.DISCOVERY_ADMIN_ORIGIN); } catch { throw new Stop('ADMIN_SYNC_CONFIGURATION_REQUIRED'); }
  // Explicit trusted deployment boundary. Do not send credentials to arbitrary input URLs.
  if (origin.href !== 'https://portfolio-api-forn.onrender.com/' || !/^[a-f0-9]{64}$/iu.test(env.DISCOVERY_IMPORT_KEY ?? ''))
    throw new Stop('ADMIN_SYNC_CONFIGURATION_REQUIRED');
  return {origin:origin.origin,secret:env.DISCOVERY_IMPORT_KEY};
}
async function request(config, path, bundle, transport) {
  const signal=AbortSignal.timeout(20_000);
  let response;
  try {
    response=await transport(new URL(path,config.origin),{method:bundle?'POST':'GET',redirect:'error',credentials:'omit',signal,
      headers:{'X-Discovery-Key':config.secret,...(bundle?{'Content-Type':'application/json'}:{})},
      ...(bundle?{body:JSON.stringify(bundle)}:{})});
    if (!response.ok) throw new Error();
    // Never surface raw response/provider diagnostics or any credentials.
    const reader=response.body?.getReader();if(!reader)throw new Error();
    const chunks=[];let size=0;
    try{while(true){const {done,value}=await reader.read();if(done)break;size+=value.byteLength;
      if(size>4096){await reader.cancel();throw new Error();}chunks.push(Buffer.from(value));}}
    finally{reader.releaseLock();}
    const text=Buffer.concat(chunks).toString('utf8');
    const body=JSON.parse(text);
    return body.data;
  } catch { throw new Stop('ADMIN_SYNC_FAILED_RETRY_SYNC_ONLY'); }
}
export async function preflightAdminSync(config, transport=fetch) {
  if((await request(config,'/api/v1/discovery-sync/ready',null,transport))?.ready!==true)throw new Stop('ADMIN_SYNC_NOT_READY');
}
export async function syncReviews(state, config, transport=fetch) {
  if(!Array.isArray(state.discoveryReviews))throw new Stop('NO_CAPTURED_REVIEW_OBSERVATIONS');
  const summaries=[];
  for(const bundle of state.discoveryReviews){
    const result=await request(config,'/api/v1/discovery-sync/import',bundle,transport);
    if(!Number.isInteger(result?.inserted)||!Number.isInteger(result?.unchanged)||result.inserted<0||result.unchanged<0||
      result.inserted+result.unchanged!==bundle.items.length)throw new Stop('ADMIN_SYNC_RECEIPT_INVALID');
    summaries.push({runId:bundle.runId,pageNumber:bundle.pageNumber,inserted:result.inserted,unchanged:result.unchanged});
  }
  // No mutation of registry/ledger or Human Review. Repeating uses identical payloads.
  state.adminSync={completedAt:new Date().toISOString(),summaries};
  return summaries;
}

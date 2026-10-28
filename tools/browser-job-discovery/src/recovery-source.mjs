import {createHash} from 'node:crypto';
import {readFile} from 'node:fs/promises';

// Audited source-byte manifests, never executable remote source or user input.
export const RECOVERY_PRODUCERS=Object.freeze({
  'afc71f0cde25d9aebe04f748edb33f35c330fc9a':{kind:'FLAT',digests:{
    core:'ce6d8aebe486ea362fe7968ae330a69133cf1aa1517a537b0d3e4bcdf2d80175',
    adapter:'61df60d384462b4e9a425e475609cf0b5860a978490c5cf6f96c9f494fe03f35',vacancy:null}},
  '81e15d15e379d4ea2c27a654a5966d056404efe5':{kind:'VACANCY',digests:{
    core:'9f3e07ef95496a52801dcf9886cdff56158ea1075f1124343e64e3ba5a99416e',
    adapter:'9e0227a424ef12ce06f24a05490d5431f7294fc3680a570696fa79fb437e5d0d',
    vacancy:'3f133f47d2d917f0580579f2afac5ce2781080791287ebb71824b74faf5c9b63'}}
});
for(const producer of Object.values(RECOVERY_PRODUCERS)){
  Object.freeze(producer.digests);Object.freeze(producer);
}
const sha=v=>createHash('sha256').update(v).digest('hex');
const logical='40482593764',repo='CoderPL1005/portfolio';
const fields=['title','employer','location','requirements','body'];
export async function recoveryProducerEvidence(api,input){
  const run=await api.json(`/actions/runs/${logical}`,128*1024);
  const producer=Object.hasOwn(RECOVERY_PRODUCERS,run.head_sha)?RECOVERY_PRODUCERS[run.head_sha]:null;
  if(!producer||String(run.id)!==logical||run.status!=='completed'||run.run_attempt!==1||
    run.head_repository?.full_name!==repo||run.head_branch!=='main')return null;
  const sourceDigests={};
  for(const [kind,file] of [['core','core.mjs'],['adapter','socialapis-adapter.mjs'],['vacancy','vacancy-extraction.mjs']]){
    if(producer.digests[kind]===null){sourceDigests[kind]=null;continue;}
    const response=await api.json(`/contents/tools/browser-job-discovery/src/${file}?ref=${run.head_sha}`,256*1024);
    if(response.encoding!=='base64'||typeof response.content!=='string'||response.content.length>300_000)return null;
    sourceDigests[kind]=sha(Buffer.from(response.content,'base64'));
  }
  if(Object.keys(producer.digests).some(k=>sourceDigests[k]!==producer.digests[k]))return null;
  // Verify local replay bytes, never evaluate downloaded code.
  if(producer.kind==='VACANCY'){
    for(const [kind,file] of [['core','core.mjs'],['vacancy','vacancy-extraction.mjs']]){
      const text=await readFile(new URL(file,import.meta.url),'utf8');
      if(sha(text.replace(/\r\n/gu,'\n'))!==producer.digests[kind])return null;
    }
  }
  const result=input.snapshots.result,page=result.orchestration.pages.at(-1);
  const returned=new Set(Array.isArray(page?.returnedPostIds)?page.returnedPostIds:[]);
  const observations=result.discoveryReviews.filter(b=>b.runId===logical&&b.pageNumber===page?.pageNumber&&b.query===page?.query).flatMap(b=>b.items);
  const records=[];
  for(const j of result.jobs){
    // An uploader SHA cannot establish an older registry row's origin.
    if(!returned.has(j.id)||typeof j.body!=='string')continue;
    const matches=observations.filter(o=>o.providerPostId===j.id&&o.rawContent===j.body&&o.contentHash===sha(j.body));
    if(matches.length!==1)continue;
    const original={...j,title:'',employer:'',location:'',requirements:'',body:matches[0].rawContent};
    records.push({sourceIdentity:j.sourceIdentity,input:original,projection:j,
      inputHash:sha(fields.map(k=>original[k]).join('\n').replace(/\r\n?/gu,'\n').normalize('NFC').trim()),originArtifactId:12723771007});
  }
  return {source:'AUTHENTICATED_GITHUB_GET',repository:input.repository,runId:logical,headSha:run.head_sha,sourceDigests,records};
}

import {createHash} from 'node:crypto';
import {SocialApisLiveDiscoveryAdapter} from './socialapis-adapter.mjs';
import {normalizeSocialApisItem,parseSocialApisSearchResponse} from './socialapis-live-smoke.mjs';
import {JobPostExtractor,ActivityBudget,Stop} from './core.mjs';
import {resolveOneRequestPolicy} from './candidate-technology-profile.mjs';
import {extractRecruitmentPost} from './ai-job-extraction.mjs';
import {buildReviewItems,reviewExport} from './discovery-review.mjs';
import {supportedPostingFacts} from './discovery-posting-facts.mjs';

const sha=value=>createHash('sha256').update(value,'utf8').digest('hex');
const fields=['companyName','positionTitle','location','employmentType','workplaceType','salaryMinimum','salaryMaximum','salaryCurrency','salaryPeriod',
  'experienceRequirements','description','technologyStack','applicationEmail','applicationUrl','expiresAt','notes'];
const uuid=value=>typeof value==='string'&&/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/iu.test(value);
function facts(values,raw,supported={},arrayEvidence=[]){
  return Object.fromEntries(fields.map(key=>{
    const value=values[key]??null;
    const quote=supported[key]?.value===value?supported[key].quote:typeof value==='string'&&raw.includes(value)?value:null;
    const spans=arrayEvidence.filter(e=>Number.isInteger(e.start)&&Number.isInteger(e.end)&&e.start>=0&&e.end>e.start&&raw.slice(e.start,e.end)===e.quote);
    const valid=value!==null&&(quote!==null||Array.isArray(value)&&value.length>0&&value.every(v=>typeof v==='string'&&spans.some(e=>e.quote.toLowerCase().includes(v.toLowerCase()))));
    const start=quote===null?-1:raw.indexOf(quote);
    return [key,{value:valid?value:null,status:value===null?'NOT_PROVIDED':valid?'EXTRACTED':'NEEDS_REVIEW',
      evidence:valid&&Array.isArray(value)?spans:quote===null?[]:[{start,end:start+quote.length,quote}]}];
  }));
}
function deterministic(observation){
  const raw=observation.rawContent;
  if(!raw||raw.length>24000||!observation.providerPostId)return {recruitment:false,vacancies:[]};
  const job=new JobPostExtractor().extract({id:observation.providerPostId,provider:'SOCIALAPIS',source:'FACEBOOK',sourceUrl:observation.sourceUrl,
    body:raw,title:'',employer:'',location:'',requirements:'',publishedAt:observation.publishedAt});
  const supported=job.vacancies?.length===1?supportedPostingFacts(raw):{};
  const vacancies=(job.vacancies??[]).map(v=>{
    const title=v.title?.length<=255&&!/[\n@]/u.test(v.title)?v.title:null;
    const values={companyName:v.employer??job.employer,positionTitle:title,location:v.location,description:raw,
      technologyStack:v.technologies??[],experienceRequirements:v.requirements??job.sharedRequirements,applicationEmail:v.contactEmail??job.contactEmail,
      applicationUrl:v.applicationUrl??job.sharedApplicationDetails?.applicationUrl,employmentType:v.employmentType,workplaceType:v.workplaceType,
      ...Object.fromEntries(Object.entries(supported).map(([key,fact])=>[key,fact.value]))};
    // Locate verbatim stack mentions only inside this extractor's role block.
    // Canonical names not literally present abstain instead of inventing quotes.
    const block=v.evidence?.body,offset=typeof block==='string'?raw.indexOf(block):-1;
    const spans=offset<0?[]:(v.technologies??[]).flatMap(technology=>{
      const start=block.toLowerCase().indexOf(technology.toLowerCase());
      return start<0?[]:[{start:offset+start,end:offset+start+technology.length,quote:raw.slice(offset+start,offset+start+technology.length)}];
    });
    return {fields:facts(values,raw,supported,spans),...values};
  });
  return {recruitment:job.structuredMatch?.recruitment===true,vacancies};
}
function observationsFrom(rows){
  return rows.map((row,itemIndex)=>{
    const mapped=row&&typeof row==='object'?normalizeSocialApisItem(row):null;
    const basic=row&&typeof row==='object'&&Object.hasOwn(row,'basic_info')?row.basic_info:row;
    const raw=typeof basic?.post_text==='string'?basic.post_text:typeof row?.text==='string'?row.text:null;
    const originalUrl=typeof basic?.url==='string'?basic.url:typeof row?.url==='string'?row.url:null;
    return {itemIndex,providerPostId:mapped?.sourceExternalId??null,sourceUrl:mapped?.sourceUrl??null,
      providerSourceUrl:originalUrl!==null&&originalUrl.length<=2048?originalUrl:null,
      rawContent:raw,contentHash:raw===null?null:sha(raw),publishedAt:mapped?.publishedAt??null,
      outcome:!row||typeof row!=='object'?'ITEM_NOT_OBJECT':mapped?.idUrlConflict?'POST_ID_URL_MISMATCH':
        !mapped?.sourceExternalId?'POST_ID_MISSING':!mapped?.sourceUrl?'POST_URL_INVALID_OR_UNSUPPORTED':!raw?'POST_TEXT_MISSING':'READY_FOR_EXTRACTION'};
  });
}
/** Backend is the durable authority. A process restart/rerun never resets claim.
 * Ports are injectable for offline tests; production always uses bounded HTTPS.
 */
export async function runDiscoveryExecution({executionId,workflowRunId,runAttempt=1},{backend,providerTransport}={}){
  if(!uuid(executionId)||!/^\d{1,20}$/u.test(String(workflowRunId)))throw new Stop('DISCOVERY_EXECUTION_ID_INVALID');
  if(Number(runAttempt)!==1)return {state:'BLOCKED',reason:'DISCOVERY_RERUN_FORBIDDEN',providerRequests:0};
  const config=await backend.getConfiguration(executionId);
  if(config.maxRequests!==1||config.maxCredits!==1||config.freePlanConfirmed!==true||config.noCashConfirmed!==true)throw new Stop('DISCOVERY_EXECUTION_LIMITS_INVALID');
  const policy=config.portfolioSnapshot?resolveOneRequestPolicy({portfolioSnapshot:config.portfolioSnapshot},config.preferences):null;
  const query=policy?.query??config.query;
  const accountingHeaders={};
  const transport=async(...args)=>{
    const response=await(providerTransport??globalThis.fetch)(...args);
    const key=process.env.SOCIALAPIS_API_KEY;
    const safeHeader=name=>{const value=response.headers.get(name);return value===null?null:value.length>128||key&&(value.includes(key)||value.includes(encodeURIComponent(key)))?'INVALID_OR_REDACTED':value;};
    accountingHeaders.deductedHeader=safeHeader('X-SocialAPIs-Credits-Deducted');
    accountingHeaders.alternateHeader=safeHeader('X-SocialAPIs-Credits-Used');
    return response;
  };
  // Validate transport/key/config BEFORE durable claim; no request has started.
  const adapter=new SocialApisLiveDiscoveryAdapter({enabled:true,freePlanConfirmed:true,noCashConfirmed:true,maxRequests:1,maxCredits:1,
    timeoutMs:Math.min(config.timeoutMs??20000,20000),query,retainAudit:true},transport);
  if(!process.env.SOCIALAPIS_API_KEY||process.env.SOCIALAPIS_API_KEY.length<16)throw new Stop('SOCIALAPIS_API_KEY_REQUIRED');
  const claim=await backend.claim(executionId);
  if(!claim.claimed)return {state:'BLOCKED',reason:'DISCOVERY_ALREADY_CLAIMED',providerRequests:0};
  const budget=new ActivityBudget({searches:1,candidates:20,durationMs:600000});let errorCode=null;
  try{await adapter.search(null,query,budget);}catch(error){errorCode=error instanceof Stop?error.code:'DISCOVERY_PROVIDER_OUTCOME_UNKNOWN';}
  const stats=adapter.requestStats;
  const payload=adapter.responseAudit?.payload;
  const bodyCostPresent=!!payload?.meta&&Object.hasOwn(payload.meta,'creditsCharged');
  const usageEvidence={bodyCostPresent,bodyCreditsCharged:bodyCostPresent?payload.meta.creditsCharged:null,
    deductedHeader:accountingHeaders.deductedHeader??null,alternateHeader:accountingHeaders.alternateHeader??null};
  const knownCost=stats.creditsDeducted!==null&&stats.creditsDeducted>=0&&stats.creditsDeducted<=1&&
    (!bodyCostPresent||typeof usageEvidence.bodyCreditsCharged==='number'&&Number.isFinite(usageEvidence.bodyCreditsCharged)&&usageEvidence.bodyCreditsCharged===stats.creditsDeducted);
  // Keep accounting failure distinct from provider/transport failure. No retry.
  const terminalErrors=new Set(['PROVIDER_ERROR','PROVIDER_HTTP_REJECTED','NO_VALID_PROVIDER_ITEMS']);
  const providerOperation=!knownCost||stats.httpStatus==null||errorCode&&!terminalErrors.has(errorCode)?'UNKNOWN':errorCode?'FAILED':'SUCCEEDED';
  if(!knownCost)errorCode='PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED';
  const usage={actualRequests:1,creditsDeducted:stats.creditsDeducted,creditsUsedAlternateHeader:stats.creditsUsedAlternateHeader??null,httpStatus:stats.httpStatus??null,
    providerOperation,errorCode,usageEvidence};
  const parsed=adapter.responseAudit?parseSocialApisSearchResponse(adapter.responseAudit.payload):null;
  const observations=observationsFrom(parsed?.rows??[]);
  // Raw goes durable before semantic generation. Failure leaves a visible claimed
  // execution; never retry provider, release claim, or use a public artifact.
  await backend.saveObservations(executionId,observations,usage);
  const results=[];const completed=[];
  for(const observation of observations){
    let extracted=providerOperation==='SUCCEEDED'?deterministic(observation):{recruitment:false,vacancies:[]},semanticError=false;
    const incomplete=!extracted.vacancies.length||extracted.vacancies.some(v=>['companyName','positionTitle','location'].some(k=>v.fields[k].value===null));
    if(incomplete&&config.semanticEnabled&&providerOperation==='SUCCEEDED'&&observation.rawContent&&observation.rawContent.length<=24000&&observation.providerPostId&&observation.outcome==='READY_FOR_EXTRACTION'){
      try{
        const ai=await extractRecruitmentPost({sourcePostId:observation.providerPostId,sourceUrl:observation.sourceUrl,rawContent:observation.rawContent,
          contentHash:observation.contentHash,publishedAt:observation.publishedAt},{provider:{modelIdentity:config.semanticModel??'backend-configured-gemini',
            extract:async()=>{
              const result=await backend.extract(executionId,{itemIndex:observation.itemIndex,rawContent:observation.rawContent,contentHash:observation.contentHash});
              if(result?.modelIdentity&&config.semanticModel&&result.modelIdentity!==config.semanticModel)throw Error('MODEL_CHANGED');
              return result?.output;
            }}});
        extracted={recruitment:ai.vacancies.length>0,vacancies:ai.vacancies.map(v=>{
          const supported=ai.vacancies.length===1?supportedPostingFacts(observation.rawContent):{};
          const values={companyName:ai.employer.value,positionTitle:v.title.value,location:v.location.value??ai.sharedLocation.value,description:observation.rawContent,
            technologyStack:v.technologies.value??[],experienceRequirements:v.requirements.value??ai.sharedRequirements.value,applicationUrl:v.applicationUrl.value??ai.applicationUrl.value,
            ...Object.fromEntries(Object.entries(supported).map(([key,fact])=>[key,fact.value]))};
          return {...values,fields:facts(values,observation.rawContent,supported,v.technologies.evidence)};
        })};
      }catch{semanticError=true;}
    }
    const vacancies=extracted.vacancies.map(v=>{
      // Only evidence-validated values go onto the ingestion wire.
      const values=Object.fromEntries(fields.map(key=>[key,v.fields[key].value]));
      return {...values,fields:v.fields,fieldStatuses:Object.fromEntries(fields.map(key=>[key,v.fields[key].status]))};
    });
    const missingFields=[...new Set(vacancies.flatMap(v=>['companyName','positionTitle','location','description'].filter(key=>!v.fields[key].value)))];
    if(!vacancies.length)missingFields.push('VACANCIES');
    const status=semanticError||missingFields.length||observation.outcome!=='READY_FOR_EXTRACTION'?'NEEDS_REVIEW':'NORMALIZED';
    const outcome=providerOperation!=='SUCCEEDED'||observation.outcome!=='READY_FOR_EXTRACTION'?observation.outcome:extracted.recruitment?observation.outcome:'INVALID_RECRUITMENT';
    results.push({itemIndex:observation.itemIndex,status,missingFields,vacancies});
    completed.push({...observation,outcome,vacancies,extractionError:semanticError?'SEMANTIC_UNAVAILABLE':null});
  }
  const state=providerOperation==='UNKNOWN'?'PROVIDER_UNKNOWN':providerOperation==='FAILED'?'FAILED':results.some(r=>r.status==='NEEDS_REVIEW')?'PARTIAL':'SUCCEEDED';
  const reviewItems=buildReviewItems(parsed?.rows??[],{profile:policy?.profile,targetJobPreferences:policy?.targetJobPreferences});
  return await backend.complete(executionId,{...usage,state,errorCode,triggerSource:config.triggerSource??'ADMIN_MANUAL',observations:completed,results,
    reviewBundle:reviewExport(reviewItems,{runId:executionId,query})});
}

export function executionBackendFromEnvironment(){
  const origin=process.env.DISCOVERY_EXECUTION_ORIGIN??'https://portfolio-api-forn.onrender.com';
  const key=process.env.DISCOVERY_WORKER_KEY,run=process.env.GITHUB_RUN_ID,attempt=process.env.GITHUB_RUN_ATTEMPT;
  if(origin!=='https://portfolio-api-forn.onrender.com'||!/^[a-f0-9]{64}$/iu.test(key??'')||!/^\d{1,20}$/u.test(run??'')||attempt!=='1')throw new Stop('DISCOVERY_WORKER_CONFIGURATION_INVALID');
  const request=async(id,suffix,body)=>{
    if(!uuid(id))throw new Stop('DISCOVERY_EXECUTION_ID_INVALID');
    const response=await fetch(`${origin}/api/v1/discovery-execution-worker/${id}/${suffix}`,{method:body===undefined?'GET':'POST',redirect:'manual',credentials:'omit',
      headers:{'X-Discovery-Worker-Key':key,'X-GitHub-Run-Id':run,'X-GitHub-Run-Attempt':attempt,'Content-Type':'application/json'},
      body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)});
    if(!response.ok)throw new Stop(response.status===409?'DISCOVERY_EXECUTION_CONFLICT':'DISCOVERY_BACKEND_UNAVAILABLE');
    const text=await response.text();if(Buffer.byteLength(text)>8*1024*1024)throw new Stop('DISCOVERY_BACKEND_RESPONSE_TOO_LARGE');
    return JSON.parse(text).data;
  };
  return {
    getConfiguration:async id=>{const data=await request(id,'config');return {...data.configuration,executionId:id,triggerSource:data.triggerSource};},
    claim:id=>request(id,'claim',{}),
    saveObservations:(id,observations,usage)=>request(id,'observations',{...usage,observations}),
    extract:(id,body)=>request(id,'extract',body),complete:(id,body)=>request(id,'results',body)
  };
}

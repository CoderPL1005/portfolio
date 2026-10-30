import {createHash} from 'node:crypto';
import {SocialApisLiveDiscoveryAdapter} from './socialapis-adapter.mjs';
import {normalizeSocialApisItem,parseSocialApisSearchResponse} from './socialapis-live-smoke.mjs';
import {JobPostExtractor,ActivityBudget,Stop} from './core.mjs';
import {resolveOneRequestPolicy} from './candidate-technology-profile.mjs';
import {extractRecruitmentPost} from './ai-job-extraction.mjs';
import {buildReviewItemsFromObservations,reviewExport} from './discovery-review.mjs';
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
      origin:'DETERMINISTIC',evidence:valid&&Array.isArray(value)?spans:quote===null?[]:[{start,end:start+quote.length,quote}]}];
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
    return {fields:facts(values,raw,supported,spans),...values,
      experienceKnown:typeof v.experience?.evidence==='string'&&raw.includes(v.experience.evidence)};
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
  const {results,completed,reviewItems}=await processObservations({executionId,observations,config,policy,providerOperation,backend});
  const state=providerOperation==='UNKNOWN'?'PROVIDER_UNKNOWN':providerOperation==='FAILED'?'FAILED':results.some(r=>r.status==='NEEDS_REVIEW')?'PARTIAL':'SUCCEEDED';
  return await backend.complete(executionId,{...usage,state,errorCode,triggerSource:config.triggerSource??'ADMIN_MANUAL',observations:completed,results,
    reviewBundle:reviewItems.every(Boolean)?reviewExport(reviewItems,{runId:executionId,query}):null});
}

async function processObservations({executionId,observations,config,policy,providerOperation,backend}){
  const classificationFailures=new Set();
  const reviewItems=buildReviewItemsFromObservations(observations,{profile:policy?.profile,targetJobPreferences:policy?.targetJobPreferences,
    onItemError:index=>classificationFailures.add(index)});
  const results=[];const completed=[];const persistenceErrors=[];
  for(const observation of observations){
    let review=reviewItems[observation.itemIndex];
    // Oversized/unsafe text may be retained in the raw layer but is not a valid
    // review payload. Never suppress that source to manufacture a prediction.
    if(review&&(review.rawContent!==observation.rawContent||review.contentHash!==observation.contentHash)){
      classificationFailures.add(observation.itemIndex);review=null;reviewItems[observation.itemIndex]=null;
    }
    let extracted={vacancies:[]},semanticError=false,processingError=classificationFailures.has(observation.itemIndex)?'CLASSIFICATION_FAILED':null;
    try { if(providerOperation==='SUCCEEDED'&&!processingError)extracted=deterministic(observation); }
    catch { processingError='DETERMINISTIC_EXTRACTION_FAILED'; }
    const saved=config.savedProcessing?.[observation.itemIndex];
    if(saved?.semanticState==='COMPLETED'&&saved.processing?.vacancies?.length&&!processingError&&
      saved.processing.vacancies.every(v=>v?.fields&&fields.every(key=>v.fields[key]&&Object.hasOwn(v.fields[key],'value'))))
      extracted={vacancies:structuredClone(saved.processing.vacancies)};
    const incomplete=!extracted.vacancies.length||extracted.vacancies.some(v=>
      ['companyName','positionTitle','location','experienceRequirements','employmentType','workplaceType','notes'].some(k=>!v.fields[k].value)||
      !v.experienceKnown||
      !v.fields.technologyStack.value?.length||
      (v.fields.salaryMinimum.value===null&&v.fields.salaryMaximum.value===null)||
      !v.fields.salaryCurrency.value||!v.fields.salaryPeriod.value||
      (!v.fields.applicationEmail.value&&!v.fields.applicationUrl.value));
    if(incomplete&&review?.contentValidity.status==='VALID_RECRUITMENT'&&config.semanticEnabled&&providerOperation==='SUCCEEDED'&&observation.rawContent&&observation.rawContent.length<=24000&&observation.providerPostId&&observation.outcome==='READY_FOR_EXTRACTION'){
      try{
        // Make classification durable even if generation or this process dies.
        if(backend.saveProcessing&&saved?.semanticState!=='COMPLETED')await backend.saveProcessing(executionId,{itemIndex:observation.itemIndex,contentHash:observation.contentHash,review,
          vacancies:extracted.vacancies.map(v=>({...Object.fromEntries(fields.map(key=>[key,v.fields[key].value])),fields:v.fields})),extractionError:processingError});
        // The backend owns current consent, model and the durable attempt/cache.
        // Never invent a model name when an older configuration omits it.
        const generation=await backend.extract(executionId,{itemIndex:observation.itemIndex,rawContent:observation.rawContent,contentHash:observation.contentHash});
        if(!generation?.modelIdentity||config.semanticModel&&generation.modelIdentity!==config.semanticModel)throw Error('MODEL_UNVERIFIED');
        const ai=await extractRecruitmentPost({sourcePostId:observation.providerPostId,sourceUrl:observation.sourceUrl,rawContent:observation.rawContent,
          contentHash:observation.contentHash,publishedAt:observation.publishedAt},{provider:{modelIdentity:generation.modelIdentity,extract:async()=>generation.output}});
        const supplemented={vacancies:ai.vacancies.map(v=>{
          const supported=ai.vacancies.length===1?supportedPostingFacts(observation.rawContent):{};
          const values={companyName:ai.employer.value,positionTitle:v.title.value,location:v.location.value??ai.sharedLocation.value,description:observation.rawContent,
            technologyStack:v.technologies.value??[],experienceRequirements:v.requirements.value??ai.sharedRequirements.value,applicationUrl:v.applicationUrl.value??ai.applicationUrl.value,
            ...Object.fromEntries(Object.entries(supported).map(([key,fact])=>[key,fact.value]))};
          const mapped=facts(values,observation.rawContent,supported,v.technologies.evidence);
          // Carry the validator's exact spans, not a new quote found elsewhere
          // in the source. Numeric salary stays under the shared strict parser.
          const inputs={companyName:ai.employer,positionTitle:v.title,location:v.location.value!==null?v.location:ai.sharedLocation,
            technologyStack:v.technologies,experienceRequirements:v.requirements.value!==null?v.requirements:ai.sharedRequirements,
            applicationUrl:v.applicationUrl.value!==null?v.applicationUrl:ai.applicationUrl};
          for(const [key,fact] of Object.entries(inputs))if(fact.value!==null){
            mapped[key]={value:fact.value,status:'EXTRACTED',origin:'GEMINI',evidence:fact.evidence};
          }
          // Preserve other validated schema facts as reviewable, verbatim
          // source information; never fabricate normalized years/units/dates.
          const extras={seniority:v.seniority,experience:v.experienceYears,benefits:ai.sharedBenefits,
            mandatorySkills:v.mandatorySkills,optionalSkills:v.optionalSkills,recruitmentStatus:ai.recruitmentStatus,
            mentionedPublishedAt:ai.publishedAt};
          for(const [key,fact] of Object.entries(extras)){
            const literal=Array.isArray(fact.value)||typeof fact.value==='string'&&fact.evidence.some(e=>e.quote.toLowerCase().includes(fact.value.toLowerCase()));
            mapped[key]={value:fact.value===null?null:literal?fact.value:fact.evidence[0]?.quote??null,
              status:fact.value===null?'NOT_PROVIDED':'EXTRACTED',origin:'GEMINI',evidence:fact.evidence};
          }
          if(mapped.experienceRequirements.value===null&&mapped.experience.value!==null)mapped.experienceRequirements=mapped.experience;
          return {...values,fields:mapped};
        })};
        // Do not erase deterministic facts when the semantic response abstains.
        if(supplemented.vacancies.length){
          for(const vacancy of supplemented.vacancies){
            const title=vacancy.fields.positionTitle.value;
            const matches=title?extracted.vacancies.filter(v=>{
              const prior=v.fields.positionTitle.value;
              return typeof prior==='string'&&(prior.toLowerCase()===title.toLowerCase()||
                extracted.vacancies.length===1&&supplemented.vacancies.length===1&&(prior.toLowerCase().includes(title.toLowerCase())||title.toLowerCase().includes(prior.toLowerCase())));
            }):[];
            const prior=matches.length===1?matches[0]:null;
            if(prior)for(const field of fields)if(prior.fields[field].value!==null){
              const conflict=vacancy.fields[field].value!==null&&JSON.stringify(vacancy.fields[field].value)!==JSON.stringify(prior.fields[field].value);
              vacancy.fields[field]={...prior.fields[field],...(conflict?{conflict:true}:{})};
            }
          }
          // Do not union a generic deterministic heading with the validated
          // role split: that would fabricate an extra vacancy for the same post.
          extracted=supplemented;
        }
      }catch{semanticError=true;}
    }
    const vacancies=extracted.vacancies.map(v=>{
      // Only evidence-validated values go onto the ingestion wire.
      const values=Object.fromEntries(fields.map(key=>[key,v.fields[key].value]));
      return {...values,fields:v.fields,fieldStatuses:Object.fromEntries(fields.map(key=>[key,v.fields[key].status]))};
    });
    const missingFields=[...new Set(vacancies.flatMap(v=>['companyName','positionTitle','location','description'].filter(key=>!v.fields[key].value)))];
    if(!vacancies.length)missingFields.push('VACANCIES');
    const status='NEEDS_REVIEW'; // Extraction is staging, never automatic JobPosting promotion.
    const outcome=providerOperation!=='SUCCEEDED'||observation.outcome!=='READY_FOR_EXTRACTION'?observation.outcome:review?.contentValidity.status==='INVALID_RECRUITMENT'?'INVALID_RECRUITMENT':observation.outcome;
    results.push({itemIndex:observation.itemIndex,status,missingFields,vacancies});
    const processed={...observation,outcome,vacancies,extractionError:semanticError?'SEMANTIC_UNAVAILABLE':processingError};
    completed.push(processed);
    // Durable per-item layer before the final result; old injectable transports
    // remain compatible. The production transport always implements this port.
    if(backend.saveProcessing){
      try{await backend.saveProcessing(executionId,{itemIndex:observation.itemIndex,contentHash:observation.contentHash,
        review,vacancies,extractionError:processed.extractionError});}
      catch{persistenceErrors.push(observation.itemIndex);config.semanticEnabled=false;}
    }
  }
  return {results,completed,reviewItems,persistenceErrors};
}

export async function replayDiscoveryExecution({executionId,confirmGeminiFreeTier=false},{backend}={}){
  if(!uuid(executionId))throw new Stop('DISCOVERY_EXECUTION_ID_INVALID');
  const snapshot=await backend.getPersisted(executionId);
  if(snapshot.id!==executionId||!/^\d{1,20}$/u.test(snapshot.workflowRunId??'')||!Number.isInteger(snapshot.version))throw new Stop('DISCOVERY_REPLAY_PROVENANCE_INVALID');
  const observations=snapshot.results.map(r=>r.observation);
  if(observations.some((o,i)=>o.itemIndex!==i||o.rawContent!==null&&sha(o.rawContent)!==o.contentHash))throw new Stop('DISCOVERY_REPLAY_SOURCE_INVALID');
  const config={...snapshot.configuration,savedProcessing:snapshot.results,semanticEnabled:confirmGeminiFreeTier===true&&snapshot.semanticReplay?.enabled===true&&typeof snapshot.semanticReplay?.modelIdentity==='string'&&!!snapshot.semanticReplay.modelIdentity.trim(),
    semanticModel:snapshot.semanticReplay?.modelIdentity??null};
  const policy=config.portfolioSnapshot?resolveOneRequestPolicy({portfolioSnapshot:config.portfolioSnapshot},config.preferences):null;
  let version=snapshot.version;
  const ports={extract:async(id,body)=>{const response=await backend.extractPersisted(id,{...body,expectedVersion:version,expectedWorkflowRunId:snapshot.workflowRunId,confirmGeminiFreeTier});version=response.version;return response.generation;},
    saveProcessing:async(id,body)=>{
      const prior=snapshot.results[body.itemIndex];
      if(!confirmGeminiFreeTier&&prior.semanticState==='COMPLETED'&&prior.processing)return;
      const response=await backend.saveProcessing(id,{...body,expectedVersion:version,expectedWorkflowRunId:snapshot.workflowRunId});version=response.version;
    }};
  const result=await processObservations({executionId,observations,config,policy,providerOperation:'SUCCEEDED',backend:ports});
  return {state:result.persistenceErrors.length?'PROCESSING_INCOMPLETE':'PROCESSED_FOR_REVIEW',providerRequests:0,observations:observations.length,failedItemIndexes:result.persistenceErrors};
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
    saveProcessing:(id,body)=>request(id,'processing',body),
    extract:(id,body)=>request(id,'extract',body),complete:(id,body)=>request(id,'results',body)
  };
}

// Uses an existing authenticated Admin session; never a worker key, provider key
// or an invented result receipt. No provider request port exists in replay.
export function persistedBackendFromEnvironment(){
  const origin=process.env.DISCOVERY_EXECUTION_ORIGIN??'https://portfolio-api-forn.onrender.com';
  const token=process.env.DISCOVERY_ADMIN_TOKEN;
  if(origin!=='https://portfolio-api-forn.onrender.com'||!token||/[\r\n]/u.test(token))throw new Stop('DISCOVERY_ADMIN_CONFIGURATION_INVALID');
  const request=async(id,suffix,body)=>{
    if(!uuid(id))throw new Stop('DISCOVERY_EXECUTION_ID_INVALID');
    const response=await fetch(`${origin}/api/v1/admin/job-hunting/discovery/executions/${id}${suffix}`,{method:body===undefined?'GET':'POST',redirect:'manual',credentials:'omit',
      headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)});
    if(!response.ok)throw new Stop(response.status===409?'DISCOVERY_EXECUTION_CONFLICT':'DISCOVERY_ADMIN_UNAVAILABLE');
    const text=await response.text();if(Buffer.byteLength(text)>8*1024*1024)throw new Stop('DISCOVERY_BACKEND_RESPONSE_TOO_LARGE');
    return JSON.parse(text).data;
  };
  return {getPersisted:id=>request(id,''),saveProcessing:(id,body)=>request(id,'/processing',body),extractPersisted:(id,body)=>request(id,'/extract-persisted',body)};
}

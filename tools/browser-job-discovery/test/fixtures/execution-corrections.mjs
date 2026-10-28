// Synthetic, offline wire-contract fixture. Never imports environment credentials.
import {runDiscoveryExecution} from '../../src/discovery-execution-worker.mjs';
import {pathToFileURL} from 'node:url';
export const executionId='11111111-1111-4111-8111-111111111111';
export async function captureExecution(scenario='success',overrides={}){
  const original=globalThis.fetch,previous=process.env.SOCIALAPIS_API_KEY;
  globalThis.fetch=()=>{throw Error('REAL_NETWORK_FORBIDDEN');};
  process.env.SOCIALAPIS_API_KEY='SYNTHETIC_OFFLINE_PROVIDER_KEY';
  let requests=0,semantic=0,saved;
  const text=scenario==='multi'?'Company: Synthetic Software\nLocation: Hanoi\n1. Fresher C# Backend Developer\nRequirements: C#\n2. Intern Python Backend Developer\nRequirements: Python':
    scenario==='incomplete'?'Hiring developers. Contact the team.':'Company: Synthetic Software\nFresher .NET Backend Developer\nLocation: Hanoi\nRequirements: C#, ASP.NET Core';
  const row={basic_info:{post_id:'9001',post_text:text,url:scenario==='invalid-url'?'https://example.invalid/not-facebook':'https://www.facebook.com/synthetic/posts/9001'}};
  const body=scenario==='provider-error'?{success:false,error:{message:'synthetic provider failure'}}:
    {data:{items:[row]},meta:{creditsCharged:scenario==='contradictory'?2:scenario==='zero'?0:1}};
  if(Object.hasOwn(overrides,'bodyCost'))body.meta.creditsCharged=overrides.bodyCost;
  if(overrides.omitBodyCost)delete body.meta.creditsCharged;
  const backend={getConfiguration:async()=>({maxRequests:1,maxCredits:1,freePlanConfirmed:true,noCashConfirmed:true,query:'Backend Fresher Hanoi',semanticEnabled:true}),
    claim:async()=>({claimed:true}),saveObservations:async(_id,observations,usage)=>{saved={...usage,observations};},
    extract:async()=>{semantic++;throw Error('SYNTHETIC_SEMANTIC_UNAVAILABLE');},complete:async(_id,result)=>result};
  try{
    const result=await runDiscoveryExecution({executionId,workflowRunId:'10001'},{backend,providerTransport:async()=>{
      requests++;const headers={'content-type':'application/json','X-SocialAPIs-Credits-Used':'6'};
      if(scenario!=='unknown')headers['X-SocialAPIs-Credits-Deducted']=overrides.headerCost??(scenario==='zero'?'0':'1');
      if(scenario==='body-read-failure')return new Response(new ReadableStream({start(controller){controller.error(new Error('SYNTHETIC_STREAM_FAILURE'));}}),{status:200,headers});
      return new Response(JSON.stringify(body),{status:200,headers});
    }});
    return {result,saved,requests,semantic};
  }finally{globalThis.fetch=original;if(previous===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=previous;}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)process.stdout.write(JSON.stringify(await captureExecution(process.argv[2])));

import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {replayDiscoveryExecution} from '../../src/discovery-execution-worker.mjs';
import {POST_FIELDS,VACANCY_FIELDS} from '../../src/ai-extraction-schema.mjs';

export async function captureEvidence(scenario='corrected',policy='V2'){
  const id='11111111-1111-4111-8111-111111111111';
  let raw='Hiring\nIntern Backend Developer\nPartner: TAX NET\nReference: 35 Lê Văn Thiêm 😀\nRequirements: C# and .NET\nBenefits: Lunch';
  if(scenario==='inline')raw=raw.replace('Hiring\nIntern Backend Developer','TAX NET tuyển dụng Intern Backend Developer');
  if(scenario==='multi'||scenario==='cross')raw+='\nIntern Frontend Developer\nRequirements: React';
  if(scenario==='three-roles')raw+='\nIntern Frontend Developer\nRequirements: React\nIntern QA Tester\nRequirements: Postman';
  if(scenario==='schema-quotes')raw+='\nExperience: 1 năm kinh nghiệm\nLevel: thực tập sinh';
  if(scenario==='ambiguous')raw+='\nReference: 35 Lê Văn Thiêm 😀';
  if(scenario==='conflict')raw='Company: Original Labs\nLocation: Hanoi\n'+raw;
  raw+='\nPublished on: 2026-10-01';
  const unknown=()=>({value:null,status:'UNKNOWN',evidence:[]});
  const fact=(value,quote=value)=>{const start=raw.indexOf(quote);return {value,status:'POST_MENTIONED',evidence:[{start,end:start+quote.length,quote}]};};
  const vacancy=(title,technology,quote)=>({...Object.fromEntries(VACANCY_FIELDS.map(k=>[k,unknown()])),title:fact(title),technologies:fact(technology,quote)});
  const output={schemaVersion:'ai-job-extraction-v1',...Object.fromEntries(POST_FIELDS.map(k=>[k,unknown()])),
    employer:fact(scenario==='fabricated'?'Fabricated Labs':'TAX NET','TAX NET'),sharedLocation:fact('35 Lê Văn Thiêm 😀'),sharedBenefits:fact('Lunch'),
    publishedAt:fact('2026-10-01'),vacancies:[{...vacancy('Intern Backend Developer',['C#','.NET'],'C# and .NET'),mandatorySkills:fact(['C#'],'Requirements: C# and .NET')}]};
  if(scenario==='schema-quotes'){
    output.recruitmentStatus=fact('ANNOUNCED_UNVERIFIED','Hiring');
    output.vacancies[0].experienceYears=fact(1,'1 năm kinh nghiệm');
    output.vacancies[0].seniority=fact('INTERN','thực tập sinh');
  }
  if(scenario==='multi'||scenario==='cross')output.vacancies.push(vacancy('Intern Frontend Developer',['React'],'React'));
  if(scenario==='three-roles'){
    output.vacancies.push(vacancy('Intern Frontend Developer',['React'],'React'));
    output.vacancies.push(vacancy('Intern QA Tester',['Postman'],'Postman'));
  }
  if(scenario==='cross')output.vacancies[0].technologies=fact(['React'],'React');
  if(scenario==='missing'){output.employer=unknown();output.sharedLocation=unknown();}
  if(scenario!=='exact'){
    const change=node=>{if(!node||typeof node!=='object')return;for(const e of node.evidence??[]){
      if(scenario==='bytes'){e.start=Buffer.byteLength(raw.slice(0,e.start));e.end=e.start+Buffer.byteLength(e.quote);}
      else {e.start=0;e.end=e.quote.length;}
      if(scenario==='rejected')e.quote='Quotation absent from source';
    }for(const v of Object.values(node))change(v);};change(output);
  }
  const generation={modelIdentity:'offline-mock-v1',cacheVersion:'synthetic',output:JSON.stringify(output),inputTokens:null,outputTokens:null};
  const observation={itemIndex:0,providerPostId:'9001',sourceUrl:'https://www.facebook.com/fixture/posts/9001',providerSourceUrl:null,
    rawContent:raw,contentHash:createHash('sha256').update(raw).digest('hex'),publishedAt:null,outcome:'READY_FOR_EXTRACTION'};
  const processing=[];let cacheReads=0;
  const original=globalThis.fetch;globalThis.fetch=()=>{throw Error('NETWORK_FORBIDDEN');};
  try{
    await replayDiscoveryExecution({executionId:id,confirmGeminiFreeTier:true},{backend:{
      getPersisted:async()=>({id,workflowRunId:'10001',version:10,configuration:{evidencePolicy:policy},semanticReplay:{enabled:true,modelIdentity:generation.modelIdentity},
        results:[{observation,semanticState:scenario==='unknown'?'UNKNOWN':'COMPLETED'}]}),
      extractPersisted:async()=>{cacheReads++;return {version:10,generation};},
      saveProcessing:async(_id,p)=>{processing.push(p);return {version:10};}
    }});
  }finally{globalThis.fetch=original;}
  return {observation,generation,processing,cacheReads};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)process.stdout.write(JSON.stringify(await captureEvidence(process.argv[2],process.argv[3])));

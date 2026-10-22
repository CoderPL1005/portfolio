import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { JobPostExtractor } from '../src/core.mjs';
import { buildCandidateTechnologyProfile, planFacebookQueries, resolveDiscoveryPolicy, queryYield } from '../src/candidate-technology-profile.mjs';
import { targetJobPreferences, preferencesFingerprint } from '../src/target-job-preferences.mjs';
import { rankBackendOpportunities } from '../src/backend-matching.mjs';
import { classifyProfile, beginExecution, claimPage, performPage, acknowledgePersistence } from '../src/pagination-orchestrator.mjs';

const prefs=targetJobPreferences(JSON.parse(await readFile(new URL('../config/backend-target-preferences.json',import.meta.url),'utf8')));
const uid=n=>`00000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
function snapshot() {
  return {schemaVersion:1,source:'PORTFOLIO_PUBLIC_API_EXPORT',
    technologies:['.NET','C#','ASP.NET Core','Python','FastAPI','Angular','SQL Server','Docker','Git','AWS'].map((name,i)=>({id:uid(i+1),name,category:i<5?'Backend':'Supporting'})),
    skills:[{id:uid(11),name:'Python',technologyId:uid(4),experienceLevel:'LEARNING'}],experiences:[],
    projects:[{id:uid(21),status:'COMPLETED',technologies:Array.from({length:10},(_,i)=>({id:uid(i+1)}))}]};
}
function profile(input=snapshot()) {
  const p=buildCandidateTechnologyProfile(input,prefs);
  return {technologies:p.technologies.filter(t=>t.level==='USED').map(t=>t.name),locations:p.locations,seniorities:p.seniorities,technologyProfile:p};
}
function raw(body,id='41001') {return {id,provider:'SOCIALAPIS',source:'FACEBOOK',sourceUrl:`https://www.facebook.com/fixture/posts/${id}`,
  title:'',employer:'',location:'',requirements:'',body,publishedAt:null,hasImage:false};}
const text=(title='Fresher .NET Backend Developer',requirements='Build REST APIs using .NET; 0–1 years experience',location='Hanoi')=>
  `Hiring ${title}\nRequirements: ${requirements}\nLocation: ${location}`;
const extract=(body,id)=>new JobPostExtractor().extract(raw(body,id));
const match=(body,p=profile(),preferences=prefs)=>classifyProfile(extract(body),p,preferences).vacancies[0].match;

// Mirrors the real Portfolio shape: no standalone .NET record. Python/FastAPI
// have project evidence; the snapshot is synthetic, not private production data.
function ecosystemSnapshot() {
  return {schemaVersion:1,source:'PORTFOLIO_PUBLIC_API_EXPORT',
    technologies:['C#','ASP.NET Core','Python','FastAPI','SQL Server'].map((name,i)=>({id:uid(i+1),name,category:i<4?'Backend':'Supporting'})),
    skills:[{id:uid(11),name:'C#',technologyId:uid(1),experienceLevel:'USED'},
      {id:uid(12),name:'ASP.NET Core',technologyId:uid(2),experienceLevel:'USED'},
      {id:uid(13),name:'Python',technologyId:uid(3),experienceLevel:'LEARNING'},
      {id:uid(14),name:'SQL Server',technologyId:uid(5),experienceLevel:'USED'}],experiences:[],
    projects:[{id:uid(21),status:'COMPLETED',technologies:[{id:uid(3)},{id:uid(4)}]}]};
}

for(const title of ['Fresher .NET Backend Developer','Fresher C#.NET Backend Developer','Fresher ASP.NET Core Backend Developer','Fresher Python Backend Developer'])
  test(`real-shaped Portfolio without .NET record: ${title}`,()=>{
    const input=ecosystemSnapshot(),before=structuredClone(input),p=profile(input);
    assert.equal(p.technologyProfile.technologies.some(t=>t.name==='.NET'),false);
    assert.equal(match(text(title,'Build REST APIs; 0–1 years experience'),p).classification,'TARGET_MATCH');
    assert.deepEqual(input,before);
  });

test('compound C#.NET is one ecosystem expression, but C# USED alone never proves generic .NET',()=>{
  const input=ecosystemSnapshot();input.skills=input.skills.filter(s=>s.name!=='C#');
  assert.equal(match(text('Fresher C#.NET Backend Developer','Build REST APIs'),profile(input)).classification,'TARGET_MATCH');
  assert.equal(match(text('Fresher Backend Developer','Build REST APIs using C# and .NET'),profile(input)).classification,'POTENTIAL_MATCH');
  for(const level of ['LEARNING','EXPLORING']) {
    const limited=ecosystemSnapshot();limited.skills.find(s=>s.name==='ASP.NET Core').experienceLevel=level;
    const p=profile(limited);
    for(const title of ['Fresher .NET Backend Developer','Fresher C#.NET Backend Developer','Fresher ASP.NET Core Backend Developer'])
      assert.equal(match(text(title,'Build REST APIs'),p).classification,'POTENTIAL_MATCH');
  }
  const csharpOnly=ecosystemSnapshot();csharpOnly.skills=csharpOnly.skills.filter(s=>s.name!=='ASP.NET Core');
  assert.equal(match(text('Fresher .NET Backend Developer','Build REST APIs'),profile(csharpOnly)).classification,'POTENTIAL_MATCH');
});

test('generic .NET compatibility is one-way and does not grant another specific framework',()=>{
  const input=ecosystemSnapshot();input.skills.find(s=>s.name==='ASP.NET Core').experienceLevel='EXPLORING';
  input.technologies.push({id:uid(6),name:'.NET',category:'Backend'});
  input.skills.push({id:uid(16),name:'.NET',technologyId:uid(6),experienceLevel:'USED'});
  assert.equal(match(text('Fresher ASP.NET Core Backend Developer','Build REST APIs'),profile(input)).classification,'POTENTIAL_MATCH');
  const frameworkPreferences={...prefs,stackGroups:[...prefs.stackGroups,{preference:'ACCEPTED',technologies:['ASP.NET MVC']}]};
  assert.equal(match(text('Fresher ASP.NET MVC Backend Developer','Build REST APIs'),profile(ecosystemSnapshot()),frameworkPreferences).classification,'POTENTIAL_MATCH');
});

test('NET salary/employer/supporting tools cannot become demonstrated .NET; Python/FastAPI levels remain independent',()=>{
  const p=profile(ecosystemSnapshot());
  const generic=match(text('Fresher Backend Developer','SQL Server and Docker; salary NET; NET PARTNERS'),p);
  assert.notEqual(generic.classification,'TARGET_MATCH');assert.deepEqual(generic.demonstratedTechnologyHits,[]);
  assert.equal(match(text('Intern Python FastAPI Backend Developer','Build REST APIs using Python and FastAPI'),p).classification,'TARGET_MATCH');
  const input=ecosystemSnapshot();input.projects=[];
  const developing=match(text('Fresher Python Backend Developer','Build REST APIs using Python'),profile(input));
  assert.equal(developing.classification,'POTENTIAL_MATCH');assert.deepEqual(developing.developingTechnologyHits,['Python']);
});

test('legacy mode without targetJobPreferences retains its original matching behavior',()=>{
  const p=profile(ecosystemSnapshot());
  for(const [title,classification] of [['Fresher .NET Backend Developer','NOT_MATCHED'],['Fresher C#.NET Backend Developer','TARGET_MATCH'],
    ['Fresher ASP.NET Core Backend Developer','TARGET_MATCH'],['Fresher Python Backend Developer','TARGET_MATCH']])
    assert.equal(classifyProfile(extract(text(title,'Build REST APIs')),p).match.classification,classification);
});

for(const [name,title,requirements,expected] of [
  ['dotnet fresher','Fresher .NET Backend Developer','Build REST APIs using .NET; 0–1 years experience','TARGET_MATCH'],
  ['csharp intern','Intern C# Backend Engineer','Build REST APIs using C#; no experience required','TARGET_MATCH'],
  ['aspnet','Fresher ASP.NET Core Backend Engineer','Build REST APIs using ASP.NET Core','TARGET_MATCH'],
  ['python equal treatment','Fresher Python Backend Developer','Build REST APIs using Python','TARGET_MATCH'],
  ['fastapi project evidence','Intern Python FastAPI Backend Engineer','Build REST APIs using Python and FastAPI','TARGET_MATCH'],
  ['generic unknown stack','Fresher Backend Developer','Build REST APIs; SQL Server and Docker','POTENTIAL_MATCH'],
  ['fullstack backend responsibility','Fresher .NET Angular Full-stack Developer','Build REST APIs using .NET and Angular','TARGET_MATCH'],
  ['fullstack backend unverified','Fresher .NET Angular Full-stack Developer','Testing and documentation','INSUFFICIENT_DATA'],
  ['frontend','Fresher Angular Frontend Developer','Angular; C# is optional','NOT_MATCHED'],
  ['incompatible title','Fresher Java Backend Developer','Java; C# is advantageous','NOT_MATCHED'],
  ['incompatible unlisted language','Intern Node.js Backend Engineer','Build REST APIs using Node.js','NOT_MATCHED'],
  ['optional title technology','Fresher Java Backend Developer (C# optional)','Build REST APIs using Java','NOT_MATCHED'],
  ['mandatory incompatible framework','Intern Python Backend Engineer','Python and Django required','NOT_MATCHED'],
  ['explicit junior','Junior .NET Backend Developer','Build REST APIs using .NET; no experience required','NOT_MATCHED'],
  ['explicit senior','Senior Python Backend Engineer','Build REST APIs using Python','NOT_MATCHED'],
  ['junior without backend proof','Junior .NET Developer','.NET','NOT_MATCHED'],
  ['high experience without backend proof','Fresher .NET Developer','.NET; 3 years experience','NOT_MATCHED'],
  ['two years fallback','Fresher .NET Backend Developer','Build REST APIs using .NET; 2 years experience','FALLBACK_MATCH'],
  ['two years lower bound','Fresher Python Backend Developer','Python; at least 2 years experience','FALLBACK_MATCH'],
  ['more than two','Fresher .NET Backend Developer','Build REST APIs using .NET; 3 years experience','NOT_MATCHED'],
  ['strictly more than two','Fresher Python Backend Developer','Python; more than 2 years experience','NOT_MATCHED'],
  ['experience prefix','Fresher Python Backend Developer','Python; Experience: 2 years','FALLBACK_MATCH'],
  ['Vietnamese bare experience','Fresher Python Backend Developer','Python; Kinh nghiệm: 2 năm','FALLBACK_MATCH'],
  ['conflicting years','Fresher Python Backend Developer','Python; no experience required; 2 years experience','INSUFFICIENT_DATA'],
  ['unknown years explicit entry','Intern FastAPI Backend Engineer','Build REST APIs using FastAPI','TARGET_MATCH'],
  ['no seniority','Python Backend Developer','Build REST APIs using Python; 0–1 years experience','INSUFFICIENT_DATA'],
  ['preferred years not mandatory','Intern .NET Backend Developer','Build REST APIs using .NET; 2 years experience preferred','TARGET_MATCH']
]) test(`backend preferences: ${name}`,()=>assert.equal(match(text(title,requirements)).classification,expected));

test('unknown experience stays UNKNOWN even when explicit entry level qualifies',()=>{
  const result=match(text('Intern Python Backend Developer','Build REST APIs using Python'));
  assert.equal(result.classification,'TARGET_MATCH'); assert.equal(result.requiredExperience.status,'UNKNOWN');assert.equal(result.requiredExperience.minimum,null);
});

test('closed recruitment and incompatible location cannot qualify',()=>{
  assert.equal(match(text()+'\nApplications closed').classification,'NOT_MATCHED');
  assert.equal(match(text()+'\nĐã tuyển đủ').reason,'RECRUITMENT_CLOSED');
  assert.equal(match(text()+'\nĐÓNG TUYỂN').reason,'RECRUITMENT_CLOSED');
  assert.equal(match(text(undefined,undefined,'HCM')).classification,'NOT_MATCHED');
  assert.equal(match(text(undefined,undefined,'')).classification,'INSUFFICIENT_DATA');
});

test('Python alone never proves FastAPI; project links can upgrade LEARNING evidence without losing declared level',()=>{
  const s=snapshot();s.projects[0].technologies=s.projects[0].technologies.filter(t=>t.id!==uid(5));
  const p=profile(s);
  assert.equal(p.technologyProfile.technologies.find(t=>t.name==='Python').level,'USED');
  assert.deepEqual(p.technologyProfile.technologies.find(t=>t.name==='Python').declaredLevels,['LEARNING']);
  assert.equal(match(text('Intern Python FastAPI Backend Developer','Python and FastAPI'),p).classification,'POTENTIAL_MATCH');
  s.projects[0].technologies=s.projects[0].technologies.filter(t=>t.id!==uid(4));
  assert.equal(match(text('Fresher Python Backend Developer','Python'),profile(s)).classification,'POTENTIAL_MATCH');
});

test('preferences are configurable, not universal .NET/person-specific matching rules',()=>{
  const other={...prefs,stackGroups:[{preference:'PREFERRED',technologies:['Java']}],seniorities:['Junior'],locations:['HCM'],preferredExperienceMax:2,fallbackExperienceMax:3};
  const p={technologies:['Java'],locations:['HCM'],seniorities:['Junior']};
  assert.equal(match(text('Junior Java Backend Developer','Build REST APIs using Java; 2 years experience','HCM'),p,other).classification,'TARGET_MATCH');
  assert.throws(()=>targetJobPreferences({...prefs,fallbackExperienceMax:0}),/INVALID_TARGET/u);
  assert.equal(match(text('Fresher .NET Full-stack Developer','Build REST APIs using .NET'),profile(),{...prefs,allowFullStack:false}).classification,'NOT_MATCHED');
  assert.equal(match(text('Fresher DotNet Backend Developer','DotNet'),profile(),{...prefs,stackGroups:[{preference:'PREFERRED',technologies:['DotNet']}]}).classification,'TARGET_MATCH');
});

test('backend query pool excludes supporting/frontend USED tools and Junior, balances stack groups including under bounded pool',()=>{
  const p=profile().technologyProfile;
  const queries=planFacebookQueries(p,[],prefs);
  assert.ok(queries.every(q=>q.query.includes('Backend')));
  assert.ok(queries.every(q=>!/Angular|SQL|Docker|Git|AWS|Junior/iu.test(q.query)));
  assert.ok(queries[0].query.includes('.NET'));assert.ok(queries[1].query.includes('Python'));
  assert.ok(queries.some(q=>q.query.includes('FastAPI')));assert.ok(queries.some(q=>q.technologyId===null));
  const capped=planFacebookQueries(p,[],{...prefs,locations:Array.from({length:16},(_,i)=>`City ${i}`)});
  assert.equal(capped.length,32);assert.ok(capped.some(q=>q.query.includes('.NET')));assert.ok(capped.some(q=>q.query.includes('Python')));
  assert.equal(new Set(capped.map(q=>q.identity)).size,capped.length);
  const resolved=resolveDiscoveryPolicy({planQueries:true,portfolioSnapshot:snapshot(),targetJobPreferences:{...prefs,locations:Array.from({length:16},(_,i)=>`City ${i}`)}},'');
  assert.equal(resolved.adaptiveQueries.length,16);
  assert.doesNotThrow(()=>beginExecution({jobs:[]},resolved,'419'));
});

test('policy resolver uses target preferences, preserves portfolio proof, and fingerprints yield separately from old targets',()=>{
  const resolved=resolveDiscoveryPolicy({planQueries:true,portfolioSnapshot:snapshot(),targetJobPreferences:prefs},'');
  assert.deepEqual(resolved.profile.seniorities,['Fresher','Intern']);assert.ok(resolved.rotatingQuery.includes('Python'));
  const q=planFacebookQueries(resolved.profile.technologyProfile,[],prefs)[0];
  const history=[{...q,normalizedQueryIdentity:q.identity,pageNumber:1,creditsDeducted:1,newRelevantOpportunities:5}];
  assert.equal(queryYield(history,q.query,q.profileFingerprint).score,null);
  assert.equal(queryYield(history,q.query,q.profileFingerprint,preferencesFingerprint(prefs)).score,5);
});

test('set-level fallback is eligible only with zero distinct primary opportunities, independently of discovery target',()=>{
  const primary=extract(text(),'41101'),fallback=extract(text('Fresher Python Backend Developer','Python; 2 years experience'),'41102');
  const enough=rankBackendOpportunities([fallback,primary],profile(),prefs,1);
  assert.equal(enough[0].classification,'TARGET_MATCH');assert.equal(enough.find(v=>v.classification==='FALLBACK_MATCH').fallbackEligible,false);
  const scarce=rankBackendOpportunities([fallback,primary],profile(),prefs,2);
  assert.equal(scarce.find(v=>v.classification==='FALLBACK_MATCH').fallbackEligible,false);
  const none=rankBackendOpportunities([fallback],profile(),prefs,5);
  assert.equal(none[0].classification,'FALLBACK_MATCH');assert.equal(none[0].fallbackEligible,true);assert.equal(none[0].recommendationTier,1);
  for(let count=1;count<=4;count++) {
    const primaries=Array.from({length:count},(_,i)=>extract(text('Fresher .NET Backend Developer',`Build REST APIs using .NET; reference ${i}`),String(41500+i)));
    const results=rankBackendOpportunities([fallback,...primaries],profile(),prefs,5);
    const deferred=results.find(v=>v.classification==='FALLBACK_MATCH');
    assert.equal(deferred.fallbackEligible,false);assert.equal(deferred.recommendationTier,4);
    assert.equal(results.filter(v=>v.classification==='TARGET_MATCH').length,count);
  }
  const rawHistory=raw(text(),'41103');
  assert.doesNotThrow(()=>rankBackendOpportunities([rawHistory],profile(),prefs,1));
  assert.equal(rawHistory.vacancies,undefined); // Re-evaluation never rewrites history.
  const repost=structuredClone(primary);repost.id='41104';repost.sourceIdentity='SOCIALAPIS:41104';
  const dedup=rankBackendOpportunities([primary,repost,fallback],profile(),prefs,2);
  assert.equal(dedup.filter(v=>v.classification==='TARGET_MATCH').length,1);
  assert.equal(dedup.find(v=>v.classification==='FALLBACK_MATCH').fallbackEligible,false);
  assert.deepEqual(primary.publishedAt,null);assert.equal(primary.structuredMatch.freshness,'UNKNOWN');
  assert.equal(dedup.length,2); // Fallback remains in registry/derived view.
});

test('backend mode requires Portfolio proof and changing preferences cannot reuse a previous continuation',async()=>{
  assert.throws(()=>beginExecution({jobs:[]},{query:'Backend Fresher Hanoi',profile:{technologies:['Python'],locations:['Hanoi'],seniorities:['Fresher']},targetJobPreferences:prefs},'420'),/PORTFOLIO_TECHNOLOGY_PROFILE_REQUIRED/u);
  const historical=extract(text(),'42001');
  const s=beginExecution({jobs:[historical],rankedOpportunities:[{classification:'NOT_MATCHED'}]},
    {query:'Backend Fresher Hanoi',profile:profile(),targetJobPreferences:prefs},'420');
  assert.equal(s.rankedOpportunities[0].classification,'TARGET_MATCH');
  assert.equal(s.jobs[0],historical);
  assert.throws(()=>beginExecution(s,{...s.orchestration.config,targetJobPreferences:{...prefs,preferredExperienceMax:0}},'421',{resumeContinuation:true}),/CONTINUATION_UNAVAILABLE|CONTINUATION_POLICY_MISMATCH/u);
});

test('pipeline applies preferences, persists fallback ranking but never counts fallback as primary yield; no raw evidence changes',async()=>{
  const p=profile(),s=beginExecution({jobs:[]},{query:'.NET Backend Fresher Hanoi',profile:p,targetJobPreferences:prefs,target:1,maxRequests:1,maxCredits:1},'410');
  const original=text('Fresher Python Backend Developer','Build REST APIs using Python; 2 years experience');
  const expected=extract(original,'41201');
  assert.equal(claimPage(s),true);
  const key='synthetic-backend-test-key';const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY=key;
  let requests=0;
  try {
    await performPage(s,{secret:key,receipt:'1',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,transport:async()=>{
      requests++;return new Response(JSON.stringify({data:{items:[{basic_info:{post_id:'41201',post_text:original,url:raw(original,'41201').sourceUrl}}],page_info:{}}}),
        {headers:{'X-SocialAPIs-Credits-Deducted':'1'}});
    }});
  } finally {if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  assert.equal(requests,1);assert.equal(s.orchestration.phase,'COMPLETED_UNCONFIRMED');acknowledgePersistence(s,'2');
  assert.equal(s.orchestration.pages[0].fallbackVacancies,1);assert.equal(s.orchestration.pages[0].newRelevantOpportunities,0);
  assert.equal(s.rankedOpportunities[0].fallbackEligible,true);
  for(const field of ['sourceIdentity','body','contentHash','publishedAt'])assert.deepEqual(s.jobs[0][field],expected[field]);
  assert.equal(s.discoveryYieldHistory[0].targetPreferencesFingerprint,preferencesFingerprint(prefs));
});

test('backend fallback-only page resumes core under identical preferences; a later primary defers fallback without changing budgets',async()=>{
  const now=new Date('2026-10-22T03:00:00Z');
  const s=beginExecution({jobs:[]},{query:'.NET Backend Fresher Hanoi',profile:profile(),targetJobPreferences:prefs,target:1,maxRequests:1,maxCredits:1},'430',{now});
  const key='synthetic-backend-pagination-key',old=process.env.SOCIALAPIS_API_KEY;
  process.env.SOCIALAPIS_API_KEY=key;let calls=0;
  const complete=async(id,body,cursor)=>{
    assert.equal(claimPage(s,now),true);
    await performPage(s,{now,secret:key,receipt:'10',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,transport:async()=>{
      calls++;return new Response(JSON.stringify({data:{items:[{basic_info:{post_id:id,post_text:body,url:raw(body,id).sourceUrl}}],page_info:{cursor}}}),
        {headers:{'X-SocialAPIs-Credits-Deducted':'1','X-SocialAPIs-Credits-Used':'999'}});
    }});
    assert.equal(s.orchestration.phase,'COMPLETED_UNCONFIRMED');acknowledgePersistence(s,'11');
  };
  try {
    await complete('43001',text('Fresher Python Backend Developer','Python; 2 years experience'),'next-backend-page');
    assert.equal(s.orchestration.stopReason,'REQUEST_BUDGET');assert.equal(s.rankedOpportunities[0].fallbackEligible,true);
    assert.throws(()=>beginExecution(structuredClone(s),{...s.orchestration.config,targetJobPreferences:{...prefs,preferredExperienceMax:0}},'431',{resumeContinuation:true,now}),/CONTINUATION_POLICY_MISMATCH/u);
    beginExecution(s,s.orchestration.config,'431',{resumeContinuation:true,now});
    await complete('43002',text(),null);
  } finally {if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  assert.equal(calls,2);assert.equal(s.requestLedger.length,2);
  assert.deepEqual(s.orchestration.pages.map(p=>p.pageNumber),[1,2]);
  assert.deepEqual(s.requestLedger.map(p=>p.creditsDeducted),[1,1]);
  assert.equal(s.orchestration.stopReason,'SUFFICIENT_NEW_VACANCIES');
  assert.equal(s.rankedOpportunities.find(v=>v.classification==='FALLBACK_MATCH').fallbackEligible,false);
  assert.equal(s.orchestration.newRelevantOpportunityIds.length,1);
  assert.doesNotMatch(JSON.stringify(s),/next-backend-page|synthetic-backend-pagination-key/u);
});

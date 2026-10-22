import test from 'node:test';
import assert from 'node:assert/strict';
import {buildCandidateTechnologyProfile,canonicalTechnology,normalizeQuery,planFacebookQueries,resolveDiscoveryPolicy} from '../src/candidate-technology-profile.mjs';
import {buildContentRegistry,opportunityIds,nearDuplicateCandidates} from '../src/content-opportunities.mjs';
import {JobPostExtractor} from '../src/core.mjs';
import {beginExecution,claimPage,performPage,acknowledgePersistence,classifyProfile,workflowMetrics} from '../src/pagination-orchestrator.mjs';

const uid=n=>`00000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
const preferences={locations:['Hanoi','Hà Nội'],seniorities:['Intern','Fresher','Junior']};
function snapshot() {return {schemaVersion:1,source:'PORTFOLIO_PUBLIC_API_EXPORT',
  technologies:[{id:uid(1),name:'C#',category:'Backend'},{id:uid(2),name:'.NET',category:'Backend'},
    {id:uid(3),name:'Python',category:'Backend'},{id:uid(4),name:'Angular',category:'Frontend'},{id:uid(5),name:'Java',category:'Backend'}],
  skills:[{id:uid(11),name:'C#',technologyId:uid(1),experienceLevel:'USED'},
    {id:uid(12),name:'.NET',technologyId:uid(2),experienceLevel:'USED'},
    {id:uid(13),name:'Python',technologyId:uid(3),experienceLevel:'LEARNING'},
    {id:uid(14),name:'Angular',technologyId:uid(4),experienceLevel:'EXPLORING'}],
  experiences:[{id:uid(21),technologies:[{id:uid(1)}]}],projects:[{id:uid(31),status:'COMPLETED',technologies:[{id:uid(4)}]}]};}
const techProfile=()=>buildCandidateTechnologyProfile(snapshot(),preferences);
function profile() {const p=techProfile();return { ...preferences,technologies:p.technologies.filter(t=>t.level==='USED').map(t=>t.name),technologyProfile:p };}
const body='Hiring Junior C#.NET Developer\nCompany: Synthetic Employer\nLocation: Hanoi\nRequirements: C#, SQL, unit testing, reliable APIs and clear communication\nApply before 2026-12-01';
function job(text=body,id='1001') {return new JobPostExtractor().extract({id,provider:'SOCIALAPIS',source:'FACEBOOK',sourceUrl:`https://facebook.com/fixture/posts/${id}`,
  title:'',employer:'',location:'',requirements:'',body:text,publishedAt:null,hasImage:false});}

test('Portfolio public snapshot uses linked USED/project/experience evidence, separates learning/exploring and ignores catalogue-only',()=>{
  const p=techProfile();assert.deepEqual(p.technologies.map(t=>t.level),['USED','USED','LEARNING','USED']);
  const angular=p.technologies.find(t=>t.name==='Angular');assert.deepEqual(angular.declaredLevels,['EXPLORING']);
  assert.ok(angular.evidence.some(e=>e.kind==='PROJECT'));assert.equal(p.technologies.some(t=>t.name==='Java'),false);
  assert.match(p.source,/NOT_LIVE_VERIFIED/u);
  const s=snapshot();s.technologies.reverse();s.skills.reverse();assert.equal(buildCandidateTechnologyProfile(s,preferences).fingerprint,p.fingerprint);
});

test('inactive/private/unlinked data cannot promote technology; malformed snapshots fail before any IO',()=>{
  const s=snapshot();s.technologies[0].isActive=false;s.skills[1].isPublished=false;s.projects[0].isPublished=false;
  s.skills.push({id:uid(99),name:'Java',experienceLevel:'USED',technologyId:null});
  assert.throws(()=>buildCandidateTechnologyProfile(s,preferences),/NO_DEMONSTRATED/u);
  for(const change of [s=>{s.skills[0].experienceLevel='EXPERT';},s=>{s.technologies.push(s.technologies[0]);},s=>{s.source='CV';},s=>{s.technologies[0].id='invalid';}]) {
    const input=snapshot();change(input);assert.throws(()=>buildCandidateTechnologyProfile(input,preferences),/INVALID_PORTFOLIO/u);
  }
});

test('a PLANNED project link is not demonstrated usage and does not upgrade EXPLORING',()=>{
  const s=snapshot();s.projects[0].status='PLANNED';
  const p=buildCandidateTechnologyProfile(s,preferences);assert.equal(p.technologies.find(t=>t.name==='Angular').level,'EXPLORING');
  assert.equal(planFacebookQueries(p).some(q=>q.query.includes('Angular')),false);
});

test('aliases are spelling-only: CSharp/C sharp work; bare NET is not .NET and C# does not imply ASP.NET Core',()=>{
  assert.equal(canonicalTechnology('CSharp'),'C#');assert.equal(canonicalTechnology('DotNet'),'.NET');assert.equal(canonicalTechnology('NET'),null);
  assert.equal(normalizeQuery('DotNet Junior Ha Noi'),normalizeQuery('.NET Junior Hà Nội'));
  const j=classifyProfile(job(body.replace('C#.NET','CSharp')),profile());assert.equal(j.match.classification,'TARGET_MATCH');
  assert.equal(j.vacancies[0].match.demonstratedTechnologyHits.includes('ASP.NET Core'),false);
});

test('planner generates bounded deduplicated multilingual candidates from evidence, not a fixed .NET production profile',()=>{
  const p=techProfile(),queries=planFacebookQueries(p);
  assert.ok(queries.length>3 && queries.length<=32);assert.equal(new Set(queries.map(q=>q.identity)).size,queries.length);
  assert.ok(queries.some(q=>q.query.includes('C#')));assert.ok(queries.some(q=>q.query.includes('Angular')));
  assert.equal(queries.some(q=>/Python|Java/u.test(q.query)),false);
  const s=snapshot();s.technologies=[{id:uid(1),name:'Node.js',category:'Backend'}];s.skills=[{id:uid(11),name:'Node.js',experienceLevel:'USED',technologyId:uid(1)}];s.projects=[];
  const other=buildCandidateTechnologyProfile(s,preferences);
  assert.ok(planFacebookQueries(other).every(q=>q.query.includes('Node.js')));
  assert.equal(classifyProfile(job(body.replace(/C#\.NET|C#/gu,'Node.js')), {...preferences,technologies:['Node.js'],technologyProfile:other}).match.classification,'TARGET_MATCH');
});

test('ranking uses profile-scoped canonical opportunities per deducted credit and reports surveyed depth, not exhaustive zero',()=>{
  const p=techProfile(),queries=planFacebookQueries(p),a=queries[0],b=queries[1];
  const rows=[{normalizedQueryIdentity:a.identity,profileFingerprint:p.fingerprint,pageNumber:1,creditsDeducted:1,newRelevantOpportunities:0},
    {normalizedQueryIdentity:b.identity,profileFingerprint:p.fingerprint,pageNumber:2,creditsDeducted:1,newRelevantOpportunities:2},
    {normalizedQueryIdentity:a.identity,profileFingerprint:'other-profile',pageNumber:4,creditsDeducted:1,newRelevantOpportunities:999}];
  const ranked=planFacebookQueries(p,rows);assert.equal(ranked[0].identity,b.identity);
  assert.equal(ranked.find(q=>q.identity===a.identity).depthSurveyed,1);assert.equal(ranked.find(q=>q.identity===a.identity).score,0);
  assert.ok(ranked.findIndex(q=>q.identity===a.identity)>ranked.findIndex(q=>q.score===null));
});

for(const [name,text,expected] of [
  ['C#.NET without ASP.NET Core',body,'TARGET_MATCH'],
  ['C# backend',body.replace('C#.NET','C# Backend'),'TARGET_MATCH'],
  ['compact C#.NET headline without ASP.NET Core','Tuyển Fresher C#.NET tại Hà Nội\nYêu cầu: có tư duy logic','TARGET_MATCH'],
  ['NET PARTNERS non-technology','NET PARTNERS tuyển Junior Sales\nLocation: Hanoi\nThu nhập NET 15 triệu','NOT_MATCHED'],
  ['salary NET not a skill','Hiring Junior Developer\nLocation: Hanoi\nSalary NET 15 triệu','INSUFFICIENT_DATA'],
  ['job seeker','Em tìm việc Junior C#.NET Developer tại Hà Nội, nhờ review CV','NOT_MATCHED'],
  ['unrelated Java React','Hiring Junior Java React Developer\nLocation: Hanoi\nRequirements: Java, React\nNice to have: C#','NOT_MATCHED'],
  ['closed posting',body+'\nĐã tuyển đủ, ngừng nhận hồ sơ.','NOT_MATCHED'],
  ['learning only','Hiring Junior Python Developer\nLocation: Hanoi\nRequirements: Python','POTENTIAL_MATCH'],
  ['missing level','Hiring C# Developer\nLocation: Hanoi\nRequirements: C#','INSUFFICIENT_DATA'],
  ['compatible experience','Hiring C# Developer\nLocation: Hanoi\nRequirements: từ 1 năm kinh nghiệm C#','POTENTIAL_MATCH']
])test(`matching: ${name}`,()=>{
  const j=job(text),hash=j.contentHash;classifyProfile(j,profile());assert.equal(j.match.classification,expected);
  assert.equal(j.contentHash,hash);assert.equal(j.publishedAt,null);assert.equal(j.match.freshness,'UNKNOWN');
});

test('multiple vacancies keep their own role stack; optional C# cannot promote a Java/React role',()=>{
  const text='Hiring in Hanoi\n1. Junior C# Developer\nRequirements: C#\n2. Junior Java React Developer\nRequirements: Java React\nNice to have: C#';
  const j=classifyProfile(job(text),profile());assert.equal(j.vacancies.length,2);
  assert.deepEqual(j.vacancies.map(v=>v.match.classification),['TARGET_MATCH','NOT_MATCHED']);
});

test('shared SQL/supporting requirements cannot promote an unsupported Java/React primary stack',()=>{
  const p={...preferences,technologies:['C#','.NET','SQL']};
  const j=classifyProfile(job('Hiring Junior Java React Developer\nLocation: Hanoi\nRequirements: Java, React, SQL'),p);
  assert.equal(j.match.classification,'NOT_MATCHED');assert.equal(j.vacancies[0].match.reason,'PRIMARY_ROLE_STACK_MISMATCH');
});

test('Internship and Intern use the same evidenced seniority, without broadening Junior to Senior',()=>{
  const p={...preferences,technologies:['C#'],seniorities:['Intern']};
  assert.equal(classifyProfile(job('Hiring C# Developer Internship\nLocation: Hanoi\nRequirements: C#'),p).match.classification,'TARGET_MATCH');
  assert.equal(classifyProfile(job('Hiring Senior C# Developer\nLocation: Hanoi\nRequirements: C#'),p).match.classification,'NOT_MATCHED');
});

test('location aliases support HCM/Ho Chi Minh independently of the Hanoi fixture',()=>{
  const p={...preferences,technologies:['C#'],locations:['HCM']};
  assert.equal(classifyProfile(job('Hiring Junior C# Developer\nLocation: Ho Chi Minh\nRequirements: C#'),p).match.classification,'TARGET_MATCH');
});

test('exact normalized reposts share content opportunity but retain every source, hash and raw body',()=>{
  const a=job(body,'1101'),b=job(body.replace('Location: Hanoi','Location:   Hanoi'),'1102');
  const raw=[a.body,b.body],hashes=[a.contentHash,b.contentHash];const registry=buildContentRegistry([a,b]);
  assert.equal(registry.groups.length,1);assert.equal(registry.groups[0].sources.length,2);
  assert.notEqual(a.contentHash,b.contentHash);assert.deepEqual([a.body,b.body],raw);assert.deepEqual([a.contentHash,b.contentHash],hashes);
  assert.equal(registry.groups[0].confidence,'EXACT_CONTENT_REPOST_GROUP');
  assert.equal(buildContentRegistry([a,job(body.replace('2026-12-01','2027-01-01'),'1103')]).groups.length,2);
  assert.equal(buildContentRegistry([a,job(body.replace('C#.NET Developer','Angular Developer'),'1104')]).groups.length,2);
});

test('shared footer and multiple vacancies do not merge roles; short/ambiguous bodies stay source scoped',()=>{
  const text=body+'\n2. Junior Angular Developer\nRequirements: Angular\nContact: careers@example.invalid';
  const a=job(text,'1201'),b=job(text,'1202');assert.equal(a.vacancies.length,2);
  const registry=buildContentRegistry([a,b]);assert.equal(registry.groups.length,2);assert.ok(registry.groups.every(g=>g.sources.length===2));
  assert.notDeepEqual(opportunityIds(job('Hiring Junior C# Developer in Hanoi','1203')),opportunityIds(job('Hiring Junior C# Developer in Hanoi','1204')));
});

test('near duplicates >=0.92 are review-only, never canonical merges; changed role/deadline stay separate',()=>{
  const a=job(body.replace('reliable APIs and clear communication','reliable APIs with clear documentation, careful teamwork, continuous improvement and transparent communication'),'1301');
  const b=job(a.body+'!','1302');
  const pairs=nearDuplicateCandidates([b],[a]);assert.equal(pairs.length,1);assert.equal(pairs[0].decision,'REVIEW_ONLY_NOT_MERGED');
  assert.equal(buildContentRegistry([a,b]).groups.length,2);
  assert.equal(nearDuplicateCandidates([job(a.body.replace('2026-12-01','2027-01-01'),'1303')],[a]).length,0);
  assert.equal(nearDuplicateCandidates([job(a.body.replace('C#.NET Developer','Angular Developer'),'1304')],[a]).length,0);
});

const now=new Date('2026-10-22T03:00:00Z');
async function page(s,posts,cursor=null) {
  assert.equal(claimPage(s,now),true);const old=process.env.SOCIALAPIS_API_KEY;process.env.SOCIALAPIS_API_KEY='synthetic-optimization-test-key';
  let count=0;
  try{await performPage(s,{now,receipt:'1',enabled:true,freePlanConfirmed:true,noCashConfirmed:true,transport:async()=>{
    count++;return new Response(JSON.stringify({data:{items:posts.map(j=>({basic_info:{post_id:j.id,post_text:j.body,url:j.sourceUrl}})),page_info:{cursor}}}),{headers:{'X-SocialAPIs-Credits-Deducted':'1'}});
  }});acknowledgePersistence(s,'2');}finally{if(old===undefined)delete process.env.SOCIALAPIS_API_KEY;else process.env.SOCIALAPIS_API_KEY=old;}
  assert.equal(count,1);
}

test('real orchestrator counts reposts once for target/yield, retains legacy registry and respects request/credit budget',async()=>{
  const legacy=job(body,'1401'),s={evidenceKind:'SOCIALAPIS_DISCOVERY_STATE',jobs:[legacy]};
  const input={query:'C# Junior Hanoi',profile:profile(),target:2,maxRequests:2,maxCredits:2};
  beginExecution(s,input,'1');await page(s,[job(body,'1402')],'next');
  assert.equal(s.orchestration.pages[0].newRelevantOpportunities,0);assert.equal(s.orchestration.pages[0].duplicateRepostRatio,1);
  await page(s,[job(body.replace('2026-12-01','2027-01-01'),'1403')],'third');
  assert.equal(s.jobs.length,3);assert.equal(s.orchestration.stopReason,'REQUEST_BUDGET');
  const m=workflowMetrics(s);assert.equal(m.newRelevantOpportunities,1);assert.equal(m.logicalExecution.newRelevantOpportunities,1);
  assert.equal(m.actualRequests,2);assert.equal(m.totalDeductedCredits,2);assert.equal(s.contentRegistry.groups.length,2);
  assert.equal(s.jobs[0].contentHash,legacy.contentHash);assert.equal(s.jobs[0].body,legacy.body);
  assert.equal(s.discoveryYieldHistory[0].profileFingerprint,profile().technologyProfile.fingerprint);
  assert.equal(s.orchestration.pages[0].verifiedRecentVacancies,0);
});

test('controlled snapshot boundary is used by planner/matcher, never persists raw CV/contact extras or re-plans a resume',()=>{
  const s=snapshot();s.cvBytes='NEVER_PERSIST_THIS';s.email='PRIVATE_CONTACT_SENTINEL';
  const policy={portfolioSnapshot:s,profile:preferences,planQueries:true};const resolved=resolveDiscoveryPolicy(policy,'',[]);
  assert.ok(resolved.query);assert.equal(resolved.profile.technologies.includes('Python'),false);
  const state=beginExecution({jobs:[]}, {...resolved,maxRequests:1,maxCredits:1},'1');
  assert.doesNotMatch(JSON.stringify(state),/PRIVATE_CONTACT_SENTINEL|NEVER_PERSIST_THIS/u);
  const next=resolveDiscoveryPolicy(policy,'',[],{resume:true,previous:state.orchestration});assert.equal(next.query,state.orchestration.config.query);
  assert.throws(()=>resolveDiscoveryPolicy({planQueries:true,profile:preferences},'',[]),/SNAPSHOT_REQUIRED/u);
  assert.throws(()=>resolveDiscoveryPolicy({...policy,planQueries:false},'',[]),/QUERY_REQUIRED_OR_ENABLE_PLANNER/u);
  assert.throws(()=>resolveDiscoveryPolicy({...policy,planQueries:'false'},'',[]),/INVALID_QUERY_PLANNER_FLAG/u);
});

test('measured alternative after two poor core pages preserves encrypted core cursor and 4-1-1 safety',async()=>{
  const p=profile(),alternative='C# jobs Hanoi',s={jobs:[],discoveryYieldHistory:[{query:alternative,normalizedQueryIdentity:normalizeQuery(alternative),
    profileFingerprint:p.technologyProfile.fingerprint,pageNumber:1,creditsDeducted:1,newRelevantOpportunities:2}]};
  const input={query:'C# Junior Hanoi',profile:p,target:10,maxRequests:3,maxCredits:3,rotatingQuery:alternative};
  beginExecution(s,input,'1');const irrelevant=id=>job('Hiring Junior Java Developer\nLocation: Hanoi\nRequirements: Java',id);
  await page(s,[irrelevant('1501')],'page-2');assert.equal(s.orchestration.plan.allocation,'CORE');
  await page(s,[irrelevant('1502')],'page-3');assert.equal(s.orchestration.plan.allocation,'ROTATING');
  assert.equal(s.orchestration.continuation.plan.pageNumber,3);
  await page(s,[job(body,'1503')]);assert.equal(s.orchestration.status,'STOPPED_RESUMABLE');
  beginExecution(s,{...input,maxRequests:1,maxCredits:1},'2',{resumeContinuation:true,now});
  assert.equal(s.orchestration.plan.pageNumber,3);assert.equal(s.orchestration.plan.allocation,'CORE');
  assert.doesNotMatch(JSON.stringify(s),/"page-3"|synthetic-optimization-test-key/u);
});

test('legacy pre-vacancy raw records are compared in memory without rewriting evidence or counting old content as new',async()=>{
  const legacy={provider:'SOCIALAPIS',id:'1601',sourceIdentity:'SOCIALAPIS:1601',body,sourceUrl:'https://facebook.com/fixture/posts/1601',contentHash:'original-legacy-hash',publishedAt:null};
  const before=JSON.stringify(legacy),s={jobs:[legacy]};
  beginExecution(s,{query:'C# Hanoi',profile:profile(),target:5,maxRequests:1,maxCredits:1},'1');
  await page(s,[job(body,'1602')]);
  assert.equal(s.orchestration.pages[0].newRelevantOpportunities,0);assert.equal(s.contentRegistry.groups.length,1);
  assert.equal(s.contentRegistry.groups[0].sources.length,2);assert.equal(JSON.stringify(s.jobs[0]),before);
});

test('two new source IDs with identical JD do not falsely satisfy a two-opportunity target',async()=>{
  const s=beginExecution({jobs:[]},{query:'C# Hanoi',profile:profile(),target:2,maxRequests:2,maxCredits:2},'1');
  await page(s,[job(body,'1701'),job(body,'1702')],'next');
  assert.equal(s.orchestration.pages[0].newRelevantVacancies,2);assert.equal(s.orchestration.pages[0].newRelevantOpportunities,1);
  assert.equal(s.orchestration.stopReason,null);assert.equal(s.orchestration.plan.pageNumber,2);
  await page(s,[job(body.replace('2026-12-01','2027-02-01'),'1703')]);
  assert.equal(s.orchestration.stopReason,'SUFFICIENT_NEW_VACANCIES');assert.equal(s.jobs.length,3);
});

test('snapshot adaptive ranking uses distinct relevant opportunities, not a large duplicate-vacancy count',async()=>{
  const p=profile(),fp=p.technologyProfile.fingerprint;
  const s={jobs:[],discoveryYieldHistory:[
    {query:'query A',normalizedQueryIdentity:normalizeQuery('query A'),profileFingerprint:fp,creditsDeducted:1,newRelevantOpportunities:0,newRelevantVacancies:999,pageNumber:1},
    {query:'query B',normalizedQueryIdentity:normalizeQuery('query B'),profileFingerprint:fp,creditsDeducted:1,newRelevantOpportunities:2,newRelevantVacancies:2,pageNumber:1}]};
  beginExecution(s,{query:'core',profile:p,target:5,maxRequests:2,maxCredits:2,adaptiveQueries:['query A','query B']},'1');
  await page(s,[]);assert.equal(s.orchestration.plan.query,'query B');assert.equal(s.orchestration.plan.allocation,'ADAPTIVE');
});

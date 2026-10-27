import { createHash } from 'node:crypto';
import { Stop } from './core.mjs';
import { targetJobPreferences, preferencesFingerprint } from './target-job-preferences.mjs';

export const fold = value => value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();
export const locationIdentity = value => fold(value).replace(/\bha noi\b/gu,'hanoi').replace(/\b(?:ho chi minh|sai gon|saigon)\b/gu,'hcm');
export const seniorityIdentity = value => ['internship','thuc tap'].includes(fold(value))?'intern':fold(value);
export const fingerprint = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
// Spelling equivalence only, NOT a technology catalogue or proficiency inference.
const spellings = [['C#', ['c#','csharp','c sharp']], ['.NET', ['.net','dotnet']],
  ['ASP.NET Core', ['asp.net core','asp net core']], ['ASP.NET', ['asp.net','asp net']],
  ['EF Core', ['ef core','entity framework core']]];
export function canonicalTechnology(name) {
  const value = fold(name).replace(/\s+/gu,' ');
  if (value === 'net') return null; // Ambiguous salary/business wording.
  return spellings.find(([,aliases]) => aliases.includes(value))?.[0] ?? name.normalize('NFC').trim();
}
export function aliasesFor(name) {
  const canonical = canonicalTechnology(name);
  return canonical ? [...new Set([canonical, name, ...(spellings.find(([key]) => key === canonical)?.[1] ?? [])])] : [];
}
const escape = value => value.replace(/[.*+?^${}()|[\]\\]/gu,'\\$&');
export function technologyPattern(name, flags = 'iu') {
  return new RegExp(`(?<![\\p{L}\\p{N}])(?:${aliasesFor(name).map(escape).join('|') || '(?!)'})(?![\\p{L}\\p{N}])`, flags);
}
const id = value => typeof value === 'string' && /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/iu.test(value);
const text = value => typeof value === 'string' && value.trim() && value.length <= 100 && !/[\p{C}]/u.test(value);
const list = (value, max = 500) => Array.isArray(value) && value.length <= max;

/** Whitelisted snapshot of EXISTING public API contracts. No network or CV input.
 * Authorship/production freshness cannot be verified from a supplied JSON file.
 */
export function buildCandidateTechnologyProfile(snapshot, { locations, seniorities }) {
  if (snapshot?.schemaVersion !== 1 || snapshot.source !== 'PORTFOLIO_PUBLIC_API_EXPORT' ||
      !list(snapshot.technologies,100) || !list(snapshot.skills) || !list(snapshot.experiences) || !list(snapshot.projects) ||
      ![locations,seniorities].every(v => list(v,100) && v.length && v.every(text))) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
  for(const collection of [snapshot.technologies,snapshot.skills,snapshot.experiences,snapshot.projects])
    if(new Set(collection.map(v=>v?.id)).size!==collection.length || collection.some(v=>!v || typeof v!=='object'))throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
  const records = new Map();
  for (const t of snapshot.technologies) {
    if (!id(t.id) || !text(t.name) || !text(t.category) || records.has(t.id)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
    // Explicit inactive/private flags are never accepted as public evidence.
    if (t.isActive === false) continue;
    const name = canonicalTechnology(t.name);
    if (name) records.set(t.id, { technologyId:t.id, name, category:t.category, declaredLevels:[], evidence:[] });
  }
  for (const s of snapshot.skills) {
    if (!id(s.id) || !text(s.name) || !['USED','LEARNING','EXPLORING'].includes(s.experienceLevel)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
    if (s.isPublished === false || s.technologyId === null) continue;
    if (!id(s.technologyId)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
    const t=records.get(s.technologyId); if (!t) continue;
    t.declaredLevels.push(s.experienceLevel); t.evidence.push({kind:'SKILL',id:s.id,level:s.experienceLevel});
  }
  for (const [kind,entities] of [['EXPERIENCE',snapshot.experiences],['PROJECT',snapshot.projects]]) for (const entity of entities) {
    if (!id(entity.id) || !list(entity.technologies,100)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
    if (entity.isPublished === false) continue;
    if (kind==='PROJECT') {
      if (!['PLANNED','IN_PROGRESS','ACTIVE','COMPLETED','ARCHIVED'].includes(entity.status)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
      if (entity.status==='PLANNED') continue; // Intention to use is not demonstrated usage.
    }
    for (const link of entity.technologies) {
      if (!id(link.id)) throw new Stop('INVALID_PORTFOLIO_TECHNOLOGY_SNAPSHOT');
      records.get(link.id)?.evidence.push({kind,id:entity.id,level:'USED'});
    }
  }
  const technologies=[...records.values()].filter(t=>t.evidence.length).map(t=>({...t,
    declaredLevels:[...new Set(t.declaredLevels)].sort(), evidence:[...new Map(t.evidence.map(e=>[`${e.kind}:${e.id}`,e])).values()].sort((a,b)=>`${a.kind}:${a.id}`.localeCompare(`${b.kind}:${b.id}`,'en')),
    level:t.evidence.some(e=>e.level==='USED')?'USED':t.declaredLevels.includes('LEARNING')?'LEARNING':'EXPLORING'
  })).sort((a,b)=>a.technologyId < b.technologyId?-1:1);
  const demonstrated=technologies.filter(t=>t.level==='USED');
  if (!demonstrated.length) throw new Stop('NO_DEMONSTRATED_PORTFOLIO_TECHNOLOGY');
  const safe={ version:'portfolio-technology-v1', source:'OPERATOR_SUPPLIED_PUBLIC_EXPORT_NOT_LIVE_VERIFIED', technologies,
    locations:[...locations],seniorities:[...seniorities] };
  return {...safe, fingerprint:fingerprint(safe)};
}

export function normalizeQuery(query) {
  let result=query;
  for (const [canonical] of [...spellings].sort((a,b)=>b[0].length-a[0].length)) result=result.replace(technologyPattern(canonical,'giu'),canonical);
  return locationIdentity(result).replace(/\s+/gu,' ');
}
export function queryYield(history, query, profileFingerprint, targetFingerprint) {
  const rows=history.filter(h=>h.profileFingerprint===profileFingerprint && h.targetPreferencesFingerprint===targetFingerprint && h.normalizedQueryIdentity===normalizeQuery(query));
  const known=rows.filter(h=>typeof h.creditsDeducted==='number' && h.creditsDeducted>0);
  const credits=known.reduce((s,h)=>s+h.creditsDeducted,0);
  return { measuredPages:rows.length, depthSurveyed:Math.max(0,...rows.map(h=>h.pageNumber??0)),
    score:credits ? known.reduce((s,h)=>s+(h.newRelevantOpportunities??0),0)/credits : null };
}
export function planFacebookQueries(profile, history=[], targetPreferences) {
  if (targetPreferences) return planBackendQueries(profile,history,targetJobPreferences(targetPreferences));
  const result=new Map();
  const used=profile.technologies.filter(t=>t.level==='USED').sort((a,b)=>
    Number(b.evidence.some(e=>e.kind==='EXPERIENCE'))-Number(a.evidence.some(e=>e.kind==='EXPERIENCE')) || (a.name<b.name?-1:1));
  const templates=[(t,loc)=>`${t.name} ${loc}`, ...profile.seniorities.slice(0,8).map(level=>(t,loc)=>`${t.name} ${level} ${loc}`),
    (t,loc)=>`Developer ${t.name} ${loc}`,(t,loc)=>`${t.name} tuyển dụng ${loc}`];
  // Round-robin templates give different evidenced technologies a turn first;
  // stop constructing strings once the bounded candidate pool is full.
  outer: for (const template of templates) for (const t of used) for (const loc of profile.locations.slice(0,4)) {
    if(result.size>=32)break outer;
    const query=template(t,loc),identity=normalizeQuery(query);if(result.has(identity))continue;
    const evidencePriority=t.evidence.some(e=>e.kind==='EXPERIENCE')?3:t.evidence.some(e=>e.kind==='PROJECT')?2:1;
    result.set(identity,{query,identity,technologyId:t.technologyId,evidencePriority,profileFingerprint:profile.fingerprint,...queryYield(history,query,profile.fingerprint)});
  }
  return [...result.values()].map(c=>({...c,rankingValue:c.score??0.25,
    rankingBasis:c.score===null?'UNMEASURED_EXPLORATION_PRIOR':'OBSERVED_NEW_RELEVANT_OPPORTUNITIES_PER_CREDIT'}))
    .sort((a,b)=>b.rankingValue-a.rankingValue || b.evidencePriority-a.evidencePriority || (a.identity<b.identity?-1:1));
}

function planBackendQueries(profile,history,prefs) {
  const result=new Map(), targetFingerprint=preferencesFingerprint(prefs);
  const add=(query,name=null) => {
    const identity=normalizeQuery(query); if(result.has(identity))return;
    const evidence=profile.technologies.find(t=>name && fold(t.name)===fold(name));
    result.set(identity,{query,identity,technologyId:evidence?.technologyId??null,
      evidenceLevel:evidence?.level??'UNVERIFIED',profileFingerprint:profile.fingerprint,
      targetPreferencesFingerprint:targetFingerprint,...queryYield(history,query,profile.fingerprint,targetFingerprint)});
  };
  // Round-robin GROUPS before technologies/levels/locations. A catalogue full of
  // supporting USED tools cannot crowd out an explicitly accepted backend stack.
  const width=Math.max(...prefs.stackGroups.map(g=>g.technologies.length));
  outer: for(const level of prefs.seniorities) for(const location of prefs.locations) for(let i=0;i<width;i++) for(const group of prefs.stackGroups) {
    if(result.size>=31)break outer;
    const name=group.technologies[i]; if(name)add(`${name} Backend ${level} ${location}`,name);
  }
  add(`Backend Developer ${prefs.seniorities[0]} ${prefs.locations[0]}`);
  return [...result.values()].map((c,index)=>({...c,explorationOrder:index,rankingValue:c.score??0.25,
    rankingBasis:c.score===null?'UNMEASURED_EXPLORATION_PRIOR':'OBSERVED_NEW_RELEVANT_OPPORTUNITIES_PER_CREDIT'}))
    .sort((a,b)=>b.rankingValue-a.rankingValue || a.explorationOrder-b.explorationOrder);
}

// Called only from init; resolving the same snapshot is stable across resumes.
export function resolveDiscoveryPolicy(policy, query, history=[], {resume=false, previous}={}) {
  const targetPreferences=policy.targetJobPreferences ? targetJobPreferences(policy.targetJobPreferences) : undefined;
  if (policy.planQueries!==undefined && typeof policy.planQueries!=='boolean') throw new Stop('INVALID_QUERY_PLANNER_FLAG');
  if (!policy.portfolioSnapshot) {
    if (policy.planQueries) throw new Stop('PORTFOLIO_SNAPSHOT_REQUIRED_FOR_QUERY_PLANNER');
    return {...policy,...(targetPreferences?{targetJobPreferences:targetPreferences}:{}),query}; // Existing explicit operator profile stays compatible.
  }
  const technologyProfile=buildCandidateTechnologyProfile(policy.portfolioSnapshot,targetPreferences ?? policy.profile??{});
  const profile={technologies:technologyProfile.technologies.filter(t=>t.level==='USED').map(t=>t.name),
    locations:technologyProfile.locations,seniorities:technologyProfile.seniorities, technologyProfile};
  const candidates=planFacebookQueries(technologyProfile,history,targetPreferences);
  // Never rerank/replace an in-progress query during continuation.
  const fixed=resume || previous && !previous.stopReason;
  if(!query?.trim() && !fixed && !policy.planQueries)throw new Stop('QUERY_REQUIRED_OR_ENABLE_PLANNER');
  return { ...policy, ...(targetPreferences?{targetJobPreferences:targetPreferences}:{}), profile, query:query?.trim() || (fixed ? previous?.config.query : candidates[0]?.query),
    rotatingQuery:policy.rotatingQuery ?? (policy.planQueries ? fixed ? previous?.config.rotatingQuery : candidates[1]?.query : null),
    adaptiveQueries:policy.adaptiveQueries ?? (policy.planQueries ? fixed ? previous?.config.adaptiveQueries : candidates.slice(2,targetPreferences?18:undefined).map(c=>c.query) : []) };
}

// V1 uses the existing Portfolio boundary, never a fabricated technology profile.
// One selected query does not imply coverage of the other planned stacks.
export function resolveOneRequestPolicy(policy, preferences, history=[]) {
  if(!policy?.portfolioSnapshot || policy.mode && policy.mode!=='development')throw new Stop('V1_PORTFOLIO_SNAPSHOT_REQUIRED');
  const resolved=resolveDiscoveryPolicy({...policy,targetJobPreferences:preferences,planQueries:true},undefined,history);
  const candidates=planFacebookQueries(resolved.profile.technologyProfile,history,resolved.targetJobPreferences);
  const selected=candidates[0];
  if(!selected)throw new Stop('V1_QUERY_PLAN_EMPTY');
  return {...resolved,rotatingQuery:null,adaptiveQueries:[],maxRequests:1,maxCredits:1,
    querySelection:{query:selected.query,rankingBasis:selected.rankingBasis,score:selected.score,
      evidenceLevel:selected.evidenceLevel,profileFingerprint:selected.profileFingerprint,
      targetPreferencesFingerprint:selected.targetPreferencesFingerprint,candidateCount:candidates.length,
      coverage:'ONE_QUERY_ONLY_NOT_ENTIRE_POOL'}};
}

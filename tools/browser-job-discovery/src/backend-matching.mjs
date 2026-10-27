import { technologyPattern, canonicalTechnology, fold, locationIdentity, seniorityIdentity } from './candidate-technology-profile.mjs';
import { targetJobPreferences } from './target-job-preferences.mjs';
import { opportunityIds } from './content-opportunities.mjs';
import { parseExperience } from './core.mjs';
import { extractVacancies } from './vacancy-extraction.mjs';

const optional = /nice to have|preferred|optional|advantage|\ba plus\b|\bbonus\b|ưu tiên|lợi thế|không bắt buộc/iu;
const closed = /đóng tuyển|đã (?:tuyển đủ|ngừng tuyển)|ngừng nhận (?:cv|hồ sơ)|(?:position|vacancy|applications?|recruitment) (?:is |are )?closed|no longer accepting/iu;
const backend = /\bback[- ]?end\b|phía máy chủ/iu;
const fullstack = /full[- ]?stack/iu;
const frontend = /front[- ]?end|\b(?:react|angular|vue)\b/iu;
const technologyIdentity = name => fold(canonicalTechnology(name) ?? name);

// C#.NET is a compound ecosystem spelling, not two independent requirements.
// Keep this interpretation local to Backend matching, not the Portfolio data.
const stackSearchText = text => text.replace(/(?<![\p{L}\p{N}])c#\s*\.net\b/giu,'.NET');
function requestedTechnologies(text, allowed) {
  let remaining=stackSearchText(text);
  const hits=new Set();
  // Prefer the specific token over overlapping broad names. A separately stated
  // generic .NET requirement elsewhere in the JD is still evaluated separately.
  for(const name of [...allowed].sort((a,b)=>b.length-a.length))
    remaining=remaining.replace(technologyPattern(name,'giu'),match=>{hits.add(name);return ' '.repeat(match.length);});
  return allowed.filter(name=>hits.has(name));
}
const evidenceSupports = (evidence, requested) => technologyIdentity(evidence)===technologyIdentity(requested) ||
  technologyIdentity(requested)==='.net' && technologyIdentity(evidence)==='asp.net core';

function requiredExperience(v, mandatory) {
  // Reuse the existing mandatory-experience parser (ranges are masked before
  // bare durations). Optional statements were removed before this boundary.
  const parsed = parseExperience(mandatory);
  const minimum = v.experience.minimum ?? parsed.minimum;
  const minimumExclusive = /(?:hơn|trên|more than|over|(?<![>])>(?!=))\s*\d+(?:[.,]\d+)?\s*(?:năm|years?|months?|tháng)/iu.test(mandatory);
  return {...v.experience, minimum, maximum:v.experience.maximum ?? parsed.maximum,
    minimumExclusive, conflict:parsed.conflict, status:minimum === null ? 'UNKNOWN' : 'KNOWN'};
}

export function classifyBackendProfile(job, profile, input) {
  const prefs = targetJobPreferences(input);
  const records = profile.technologyProfile?.technologies ?? profile.technologies.map(name => ({name,level:'USED'}));
  const allowed = [...new Set(prefs.stackGroups.flatMap(g => g.technologies))];
  const used = records.filter(t => t.level === 'USED').map(t => t.name);
  const developing = records.filter(t => t.level !== 'USED').map(t => t.name);
  for (const v of job.vacancies) {
    const prior = v.match;
    const extractionReason = prior.extractionReason ?? prior.reason;
    const mandatory = (v.requirements ?? '').split(/\n|;|[.!?](?=\s)/u).filter(line => !optional.test(line)).join('\n');
    const title = v.title ?? '';
    const titleStack = title.split(/[();]|[.!?](?=\s)/u).filter(clause => !optional.test(clause)).join('\n');
    const duties=(v.backendResponsibilities??'').split(/\n|;/u).filter(line=>!optional.test(line)).join('\n');
    const roleText = [titleStack,mandatory,duties].join('\n');
    const body = [v.evidence.body,job.postSections?.introduction,job.postSections?.sharedFooter].filter(Boolean).join('\n');
    const requested = requestedTechnologies(roleText,allowed);
    // Directional compatibility: specific ASP.NET Core usage supports generic
    // .NET, never the reverse; C# alone does not grant generic .NET evidence.
    const demonstrated = requested.filter(name => used.some(t => evidenceSupports(t,name)));
    const learning = requested.filter(name => developing.some(t => evidenceSupports(t,name)));
    const unverified = requested.filter(name => !demonstrated.includes(name));
    // A matching optional C# or supporting SQL/Docker never rescues a Java role.
    // Lexical role-stack signals, not proficiency claims or a new catalogue.
    const explicitStack = roleText.match(/\b(?:node\.js|nodejs|php|ruby|golang|java|kotlin|spring|django)\b/giu) ?? [];
    const knownStack = [...new Set([...explicitStack,...(v.primaryTechnologies ?? []),...records.filter(t => /backend|language|framework/iu.test(t.category ?? '')).map(t => t.name)])];
    const supporting = /^(?:sql(?: server)?|docker|git|aws|azure|ef core|typescript|angular|react)$/iu;
    // Extractor may emit ASP.NET and .NET for the same ASP.NET Core token.
    // Suppressing that overlapping lexical hit does not grant framework evidence.
    const competing = knownStack.filter(name => !supporting.test(name) && technologyPattern(name).test(stackSearchText(titleStack)) && !allowed.some(a => technologyPattern(name).test(a)));
    const incompatibleMandatory = explicitStack.filter(name => !allowed.some(a => technologyIdentity(a) === technologyIdentity(name)));
    const isFullStack = fullstack.test(title);
    const responsibilities=[mandatory,duties].join('\n');
    const backendResponsibilities = backend.test(responsibilities) || /(?:build|develop|implement|xây dựng|phát triển|thiết kế).*(?:rest\s*apis?|server|api|backend|back-end)/iu.test(responsibilities);
    const hasBackend = isFullStack ? prefs.allowFullStack && backendResponsibilities : backend.test(title) ||
      /developer|engineer|lập trình/iu.test(title) && !frontend.test(title) && backendResponsibilities;
    const isClosed = closed.test(body);
    const location = prefs.locations.some(l => locationIdentity(v.location ?? '').includes(locationIdentity(l)));
    const level = v.seniority.some(l => prefs.seniorities.some(p => seniorityIdentity(l) === seniorityIdentity(p)));
    const forbiddenLevel = v.seniority.some(l => !prefs.seniorities.some(p => seniorityIdentity(l) === seniorityIdentity(p)));
    const experience = requiredExperience(v,mandatory);
    let classification = 'NOT_MATCHED', reason = 'NON_TARGET_ROLE';
    if (isClosed) reason = 'RECRUITMENT_CLOSED';
    else if (['ROLE_MISMATCH','CANDIDATE_SEEKING_WORK','RECRUITMENT_UNVERIFIED'].includes(extractionReason)) reason = extractionReason;
    else if (frontend.test(title) && !backend.test(title) && !isFullStack) reason = 'FRONTEND_ONLY';
    else if (competing.length || incompatibleMandatory.length || !requested.length && (v.primaryTechnologies ?? []).some(name => !supporting.test(name))) reason = 'INCOMPATIBLE_PRIMARY_STACK';
    else if (experience.minimum > prefs.fallbackExperienceMax || experience.minimumExclusive && experience.minimum >= prefs.fallbackExperienceMax) reason = 'REQUIRED_EXPERIENCE_EXCEEDED';
    else if (forbiddenLevel) reason = 'SENIORITY_MISMATCH';
    else if (isFullStack && !prefs.allowFullStack) reason = 'FULLSTACK_EXCLUDED';
    else if (isFullStack && !hasBackend) { classification='INSUFFICIENT_DATA'; reason='FULLSTACK_BACKEND_UNVERIFIED'; }
    else if (!hasBackend) { classification='INSUFFICIENT_DATA'; reason='BACKEND_ROLE_UNVERIFIED'; }
    else if (!location) { classification = !v.location || prior.locationAssignmentAmbiguous ? 'INSUFFICIENT_DATA' : 'NOT_MATCHED'; reason='LOCATION_UNVERIFIED_OR_MISMATCH'; }
    else if (experience.conflict) { classification='INSUFFICIENT_DATA'; reason='REQUIRED_EXPERIENCE_CONFLICT'; }
    else if (!requested.length) { classification='POTENTIAL_MATCH'; reason='BACKEND_STACK_UNVERIFIED'; }
    else if (!demonstrated.length || unverified.length) { classification='POTENTIAL_MATCH'; reason=learning.length ? 'DEVELOPING_TECHNOLOGY_ONLY' : 'STACK_EVIDENCE_INCOMPLETE'; }
    else if (!level) { classification='INSUFFICIENT_DATA'; reason='SENIORITY_UNVERIFIED'; }
    else if (experience.minimum !== null && (experience.minimum > prefs.preferredExperienceMax || experience.minimumExclusive && experience.minimum >= prefs.preferredExperienceMax)) { classification='FALLBACK_MATCH'; reason='FALLBACK_REQUIRED_EXPERIENCE'; }
    else { classification='TARGET_MATCH'; reason='DEMONSTRATED_BACKEND_ENTRY_ROLE'; }
    v.match = {...prior,classification,reason,extractionReason,profileMatched:classification==='TARGET_MATCH',
      demonstratedTechnologyHits:demonstrated,developingTechnologyHits:learning,unverifiedStack:unverified,
      requiredExperience:experience,backend:hasBackend,closed:isClosed};
  }
  const classification = ['TARGET_MATCH','POTENTIAL_MATCH','FALLBACK_MATCH','INSUFFICIENT_DATA'].find(c => job.vacancies.some(v => v.match.classification === c)) ?? 'NOT_MATCHED';
  job.structuredMatch={...job.structuredMatch,classification}; job.match={...job.structuredMatch};
  return job;
}

// Set-level recommendation. Fallback never inflates primary yield/stop counters.
export function rankBackendOpportunities(jobs, profile, prefs, _target) {
  const opportunities = new Map();
  for (const stored of jobs) {
    const copy=structuredClone(stored);
    if(!Array.isArray(copy.vacancies)) Object.assign(copy,extractVacancies(copy.body??'',copy));
    const job = classifyBackendProfile(copy,profile,prefs);
    const ids = opportunityIds(job);
    job.vacancies.forEach((v,index) => {
      if (!opportunities.has(ids[index])) opportunities.set(ids[index],{opportunityId:ids[index],sourceIdentity:job.sourceIdentity,vacancyIndex:index,classification:v.match.classification});
    });
  }
  const primary = [...opportunities.values()].filter(v => v.classification === 'TARGET_MATCH').length;
  return [...opportunities.values()].map(v => {
    // Discovery's yield target is not permission to offer a two-year fallback.
    const fallbackEligible=v.classification==='FALLBACK_MATCH' && primary === 0;
    const recommendationTier=v.classification==='TARGET_MATCH'?0 : fallbackEligible?1 :
      v.classification==='POTENTIAL_MATCH'?2 : v.classification==='INSUFFICIENT_DATA'?3 : v.classification==='FALLBACK_MATCH'?4 : 5;
    return {...v,fallbackEligible,recommendationTier};
  }).sort((a,b) => a.recommendationTier-b.recommendationTier || a.opportunityId.localeCompare(b.opportunityId,'en'));
}

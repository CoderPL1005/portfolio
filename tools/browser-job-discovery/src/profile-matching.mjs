import { canonicalTechnology, technologyPattern, fold, locationIdentity, seniorityIdentity } from './candidate-technology-profile.mjs';

const optional = /nice to have|preferred|optional|advantage|\ba plus\b|\bbonus\b|là (?:một )?lợi thế|ưu tiên|không bắt buộc/iu;
const closed = /đã (?:đóng tuyển|tuyển đủ|ngừng tuyển)|ngừng nhận (?:cv|hồ sơ)|(?:position|vacancy|applications?) (?:is |are )?closed|no longer accepting/iu;
export function classifyCandidateProfile(job, profile) {
  const records=profile.technologyProfile?.technologies;
  const used=records ? records.filter(t=>t.level==='USED').map(t=>t.name) : profile.technologies;
  const developing=records?.filter(t=>t.level!=='USED').map(t=>t.name) ?? [];
  for (const v of job.vacancies) {
    const prior=v.match; const reason=prior.extractionReason ?? prior.reason;
    const primary=(v.primaryTechnologies??[]).map(canonicalTechnology);
    const mandatory=(v.requirements??'').split(/\n|;/u).filter(line=>!optional.test(line)).join('\n');
    // Compact recruitment headlines without a recognized role title are evidence,
    // unlike arbitrary footer/body occurrences or an optional C# mention.
    const headline=!v.title ? (v.evidence.body??'').split('\n').filter(line=>/^(?:tuyển|hiring|recruitment)/iu.test(line.trim()) && !optional.test(line)).join('\n') : '';
    const roleText=[v.title,mandatory,headline].filter(Boolean).join('\n');
    const hits=names=>names.filter(name=>primary.includes(canonicalTechnology(name)) || technologyPattern(name).test(roleText));
    const usedHits=hits(used), learningHits=hits(developing);
    const titleHasStack=v.technologies.some(name=>technologyPattern(name).test(v.title??''));
    const titleSupportsProfile=[...used,...developing].some(name=>technologyPattern(name).test(v.title??'') ||
      canonicalTechnology(name)==='.NET' && /asp\.net|\.net|dotnet/iu.test(v.title??''));
    const level=v.seniority.some(l=>profile.seniorities.some(p=>seniorityIdentity(l)===seniorityIdentity(p)));
    const location=profile.locations.some(l=>locationIdentity(v.location??'').includes(locationIdentity(l))) ||
      prior.locationVerified && profile.locations.some(l=>['hanoi','ha noi'].includes(fold(l)));
    const isClosed=closed.test([v.evidence.body,job.postSections?.introduction,job.postSections?.sharedFooter].filter(Boolean).join('\n'));
    let classification='NOT_MATCHED', why='PROFILE_TECHNOLOGY_MISMATCH';
    if (isClosed) why='RECRUITMENT_CLOSED';
    else if (/(?:tuyển(?: dụng)?|hiring)\s+(?:(?:junior|fresher|intern)\s+)?(?:sales|marketing|accountant|nhân viên kinh doanh|kế toán)\b/iu.test(v.evidence.body??'')) why='NON_TARGET_ROLE';
    else if (['ROLE_MISMATCH','CANDIDATE_SEEKING_WORK','RECRUITMENT_UNVERIFIED'].includes(reason)) why=reason;
    else if (titleHasStack && !titleSupportsProfile) why='PRIMARY_ROLE_STACK_MISMATCH';
    else if (!usedHits.length && !learningHits.length) {
      if (!primary.length && !v.technologies.length) { classification='INSUFFICIENT_DATA';why='TECHNOLOGY_UNVERIFIED'; }
    } else if (!location) {
      if (!v.location || prior.locationAssignmentAmbiguous) { classification='INSUFFICIENT_DATA';why='LOCATION_UNVERIFIED'; }
      else why='LOCATION_MISMATCH';
    } else if (v.experience.minimum>2 && profile.seniorities.every(l=>/intern|fresher|junior|entry|thực tập/iu.test(l))) why='SENIORITY_MISMATCH';
    else if (!level && v.seniority.length) why='SENIORITY_MISMATCH';
    else if (!usedHits.length) { classification='POTENTIAL_MATCH';why='DEVELOPING_TECHNOLOGY_ONLY'; }
    else if (level) { classification='TARGET_MATCH';why='DEMONSTRATED_ROLE_TECHNOLOGY'; }
    else if (v.experience.minimum!==null && v.experience.minimum<=2) { classification='POTENTIAL_MATCH';why='COMPATIBLE_EXPERIENCE'; }
    else { classification='INSUFFICIENT_DATA';why='SENIORITY_UNVERIFIED'; }
    v.match={...prior,extractionReason:reason,reason:why,classification,profileMatched:classification==='TARGET_MATCH',
      demonstratedTechnologyHits:usedHits,developingTechnologyHits:learningHits,closed:isClosed};
  }
  const classification=['TARGET_MATCH','POTENTIAL_MATCH','INSUFFICIENT_DATA'].find(c=>job.vacancies.some(v=>v.match.classification===c))??'NOT_MATCHED';
  job.structuredMatch={...job.structuredMatch,classification};job.match={...job.structuredMatch};
  return job;
}

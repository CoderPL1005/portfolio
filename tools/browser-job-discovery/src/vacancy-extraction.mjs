// Pure, bounded, deterministic extraction. No provider calls or inferred facts.
export const EXTRACTION_VERSION = 'vacancy-aware-v1.1';
const unique = values => [...new Set(values)];
const clean = value => value.normalize('NFC').replace(/\r\n?/gu, '\n').replace(/[*`]/gu, '').trim();
const signalText = text => text.replace(/[\p{L}\p{N}._+%-]+@[\p{L}\p{N}.-]+\.[a-z]{2,}/giu, '').replace(/https?:\/\/\S+/giu, '');
const roles = /developer|engineer|lập trình|\b(?:pqa|qa|tester|flutter|data label(?:ing)?)\b|nghiên cứu|an ninh|bảo mật/iu;
const entry = /\b(?:intern(?:ship)?|fresher|junior|entry[- ]level)\b|thực tập/giu;
const seniority = /\b(?:intern(?:ship)?|fresher|junior|entry[- ]level|middle|mid[- ]level|senior|lead|experienced)\b|thực tập/giu;
const cities = /(?<![\p{L}\p{N}])(?:hà nội|ha noi|hanoi|hồ chí minh|ho chi minh|hcm|sài gòn|sai gon)(?![\p{L}\p{N}])/giu;
const hanoiDistrict = /nam từ liêm|bắc từ liêm|cầu giấy|thanh xuân|hoàng mai|long biên|hà đông|ba đình|hoàn kiếm|đống đa|hai bà trưng|tây hồ/iu;

function location(text) {
  text = signalText(text);
  const evidence = text.split('\n').filter(line => line.match(cities)?.length || hanoiDistrict.test(line));
  const all = unique((text.match(cities) ?? []).map(city => /hà nội|ha noi|hanoi/iu.test(city) ? 'HANOI' : 'HCM'));
  if (hanoiDistrict.test(text) && !all.includes('HANOI')) all.push('HANOI');
  return { value: all.length === 1 ? evidence.join('\n') || null : null, city: all.length === 1 ? all[0] : null,
    ambiguous: all.length > 1, evidence };
}

function technologies(text) {
  text = signalText(text);
  // Bare NET is deliberately never a technology; salary NET cannot qualify.
  const patterns = [['ASP.NET Core', /asp\.net\s*core/iu], ['ASP.NET', /asp\.net/iu], ['.NET', /\.net\b|\bdotnet\b/iu],
    ['C#', /c\s*#|c sharp/iu], ['Angular', /\bangular\b/iu], ['TypeScript', /\btypescript\b/iu],
    ['EF Core', /\bef\s*core\b|entity framework core/iu], ['SQL Server', /sql server/iu], ['SQL', /\bsql\b/iu],
    ['Azure', /\bazure\b/iu], ['Flutter', /\bflutter\b/iu], ['Python', /\bpython\b/iu], ['FastAPI', /\bfastapi\b/iu], ['C/C++', /c\s*\/\s*c\+\+/iu],
    ['Java', /\bjava\b/iu], ['React', /\breact\b/iu]];
  return patterns.filter(([, pattern]) => pattern.test(text)).map(([name]) => name);
}

function experience(text) {
  const mixed = text.match(/(\d+)\s*tháng\s*(?:đến|tới|[-–])\s*(dưới\s*)?(\d+)\s*năm/iu);
  if (mixed) return { minimum: Number(mixed[1]) / 12, maximum: Number(mixed[3]), maximumExclusive: !!mixed[2], evidence: mixed[0] };
  const range = text.match(/(\d+)\s*[-–—]\s*(\d+)\s*(years?|năm|months?|tháng)/iu);
  if (range) return { minimum: Number(range[1]) / (/month|tháng/iu.test(range[3]) ? 12 : 1),
    maximum: Number(range[2]) / (/month|tháng/iu.test(range[3]) ? 12 : 1), maximumExclusive: false, evidence: range[0] };
  const lower = text.match(/(?:từ|ít nhất|at least|minimum|>=)\s*(\d+)\s*(years?|năm|months?|tháng)|\b(\d+)\+\s*(years?|năm|months?|tháng)/iu);
  if (lower) return { minimum: Number(lower[1] ?? lower[3]) / (/month|tháng/iu.test(lower[2] ?? lower[4]) ? 12 : 1), maximum: null, evidence: lower[0] };
  if (/không (?:yêu cầu|cần) kinh nghiệm|no experience/iu.test(text)) return { minimum: 0, maximum: null, evidence: text.match(/[^\n]*(?:không (?:yêu cầu|cần) kinh nghiệm|no experience)[^\n]*/iu)[0] };
  return { minimum: null, maximum: null, evidence: null };
}

function salary(text, scope) {
  const evidence = text.split('\n').filter(line => /salary|lương|thu nhập|offer|up to|\d\s*(?:m\b|triệu)/iu.test(line));
  if (!evidence.length) return null;
  const raw = evidence.join('\n');
  const ranges = [...raw.matchAll(/(\d+(?:[.,]\d+)?)\s*[-–]\s*(\d+(?:[.,]\d+)?)\s*(m\b|triệu)/giu)];
  const lower = raw.match(/(?:từ|from)\s*(\d+(?:[.,]\d+)?)\s*(m\b|triệu)/iu);
  const upper = raw.match(/(?:up to|tối đa|đến)\s*(\d+(?:[.,]\d+)?)\s*(m\b|triệu)/iu);
  const number = v => Number(v.replace(',', '.')) * 1_000_000;
  const oneStatement = evidence.length === 1;
  return { raw, minimum: oneStatement && ranges.length === 1 ? number(ranges[0][1]) : oneStatement && lower && !upper ? number(lower[1]) : null,
    maximum: oneStatement && ranges.length === 1 ? number(ranges[0][2]) : oneStatement && upper && !lower ? number(upper[1]) : null,
    currency: /\$|usd/iu.test(raw) ? null : /vnd|vnđ|đồng|triệu/iu.test(raw) ? 'VND' : null,
    scope, appliesToAllSeniorityLevels: false };
}

function contacts(text) {
  return { contactEmail: text.match(/[\p{L}\p{N}._+%-]+@[\p{L}\p{N}.-]+\.[a-z]{2,}/iu)?.[0] ?? null,
    contactPhone: text.match(/(?:phone|điện thoại|sđt|zalo|hotline)\s*[:：-]?\s*((?:\+84|0)[\d .()-]{8,16}\d)/iu)?.[1]?.trim() ?? null,
    contactMethod: /\binbox\b|nhắn tin/iu.test(text) ? 'Inbox' : /\bzalo\b/iu.test(text) ? 'Zalo' : /@/u.test(text) ? 'Email (unverified)' : null };
}

const labelText = line => line.replace(/^[^\p{L}\p{N}.]+/u, '').trim();
const requirementsLabel = /^(?:yêu cầu|requirements?|qualifications?|bạn phù hợp nếu|điều kiện)/iu;
const benefitsLabel = /^(?:quyền lợi|benefits?|phúc lợi|chế độ|đãi ngộ|khi tham gia|bạn sẽ được)/iu;
const footerLabel = /^(?:địa chỉ|địa điểm|làm việc tại|address|office|location|liên hệ|contact|email|send cv|gửi cv|ứng tuyển|quan tâm|cảm ơn|thank|inbox|zalo|facebook\s*:)/iu;
const optionalSkill = /nice to have|preferred|optional|advantage|\ba plus\b|\bbonus\b|là (?:một )?lợi thế|ưu tiên|không bắt buộc/iu;
const responsibilitiesLabel = /^(?:responsibilities|duties|mô tả công việc|trách nhiệm|công việc)/iu;

function responsibilities(lines) {
  let active=false;const evidence=[];
  for(const line of lines){
    const text=labelText(line);
    if(responsibilitiesLabel.test(text)){active=true;const inline=text.match(/[:：]\s*(.+)$/u)?.[1];if(inline)evidence.push(inline);continue;}
    if(requirementsLabel.test(text)||benefitsLabel.test(text)||footerLabel.test(text)){active=false;continue;}
    if(active && text)evidence.push(line);
  }
  return evidence.join('\n')||null;
}

// Verbatim statements, not inferred dates, contractual terms or verified hiring.
function applicationDetails(text) {
  const lines=text.split('\n');
  const applicationLine=lines.find(line=>/apply|application|ứng tuyển|gửi (?:cv|hồ sơ)/iu.test(line) && /https?:\/\//u.test(line));
  let applicationUrl=null;
  if(applicationLine){try{const url=new URL(applicationLine.match(/https?:\/\/[^\s<>]+/u)[0]);
    if(['https:','http:'].includes(url.protocol)&&!url.username&&!url.password)applicationUrl=url.href;}catch{ /* Unsupported URL stays unknown. */ }}
  return {applicationUrl,deadline:lines.find(line=>/deadline|hạn (?:chót|nộp)|closing date/iu.test(line))??null,
    employmentType:lines.find(line=>/\bfull[- ]time\b|\bpart[- ]time\b|toàn thời gian|bán thời gian/iu.test(line))??null,
    workplaceType:lines.find(line=>/\b(?:remote|hybrid|on[- ]?site)\b|làm việc (?:từ xa|tại văn phòng)/iu.test(line))??null};
}

function extractRequirements(lines) {
  let section = null;
  const result = [];
  for (const line of lines) {
    const text = labelText(line);
    if (benefitsLabel.test(text)) { section = 'BENEFITS'; continue; }
    if (responsibilitiesLabel.test(text)) { section = 'RESPONSIBILITIES'; continue; }
    if (footerLabel.test(text) || location(text).city && /^(?:\d+\s|địa|office|address|location)/iu.test(text)) { section = 'FOOTER'; continue; }
    if (requirementsLabel.test(text)) {
      section = 'REQUIREMENTS';
      const inline = text.match(/[:：]\s*(.+)$/u)?.[1];
      if (inline) result.push(inline);
      continue;
    }
    if (!text || section === 'BENEFITS' || section === 'FOOTER' || section === 'RESPONSIBILITIES' || salary(text, 'CHECK') || location(text).city || contacts(text).contactEmail) continue;
    if (/^(?:được |hỗ trợ|cơ hội|thưởng|lộ trình|đào tạo|training|free lunch|chúng |môi trường|nếu bạn|các bạn)/iu.test(text)) continue;
    if (section === 'REQUIREMENTS' || /^(?:có |biết |nắm |hiểu |thành thạo|kiến thức|kinh nghiệm|tư duy|sẵn sàng|là sv|sv |sinh viên|đã có|đã tốt nghiệp|đã có thể|tiếng |đi làm|[≥>]\s*\d|\d+\s*(?:\+|tháng|năm)|experience|must |proficien|familiar|mandatory)/iu.test(text)) result.push(line);
  }
  return result.join('\n') || null;
}

function mandatorySkills(text) {
  return text.split(/\n|;/u).filter(clause => !optionalSkill.test(clause)).join('\n');
}

function employerMention(text) {
  const explicit = [...text.matchAll(/(?:công ty|company|employer)\s*[:：]\s*([^\n]+)/giu)].map(match => ({ value: match[1], evidence: match[0] }));
  const self = text.split('\n').map(line => labelText(signalText(line).replace(/\[[^\]]+\]\s*/gu, ''))).flatMap(line => {
    const match = line.match(/^([A-ZĐ][\p{L}\p{N}& .-]{1,60}?)\s+(?:đang\s+)?(?:tìm kiếm|tìm|tuyển(?: dụng)?)\s/u);
    return match && !/^(?:chúng|mình|tôi|em|hiện|nếu|bạn|các bạn|ứng viên|hà nội|hanoi|hcm)(?:\s|$)|^công ty$/iu.test(match[1]) ? [{ value: match[1].trim(), evidence: line }] : [];
  });
  const mentions = [...explicit, ...self];
  const names = unique(mentions.map(mention => mention.value));
  return names.length === 1 ? { value: names[0], status: 'POST_MENTIONED_UNVERIFIED', evidence: mentions.map(mention => mention.evidence) }
    : { value: null, status: null, evidence: mentions.map(mention => mention.evidence) };
}

function isHeading(line) {
  const candidate = labelText(line).replace(/^\d+[.)]\s*/u, '');
  const explicitRoleTitle = /^(?:(?:intern(?:ship)?|fresher|junior|middle|senior)[\s/–-]+)*(?:(?:software|full[- ]?stack|backend|frontend|web|automation)[\s/–-]+)*(?:(?:\.net|asp\.net(?: core)?|c#|java|react|angular|flutter)[\s/–-]+)*(?:developer|engineer|pqa|qa|tester|lập trình)/iu.test(candidate);
  // A role mentioned in a sentence is not itself a role-title heading.
  if (/\b(?:mentors?|mentoring|mentored|coaches?|coaching)\b|(?:hướng dẫn|kèm cặp|dẫn dắt)\s+(?:nhân viên|các bạn|staff|team)/iu.test(candidate)) return false;
  if (!explicitRoleTitle && /[,，]|[.!。]\s*$|thích công việc|cẩn thận|trách nhiệm/iu.test(candidate) &&
      !/tuyển|\bhiring\b|recruitment/iu.test(candidate)) return false;
  if (requirementsLabel.test(candidate) || benefitsLabel.test(candidate) || footerLabel.test(candidate) ||
      optionalSkill.test(candidate) || !explicitRoleTitle && /kinh nghiệm|kiến thức|hiểu biết|quy trình|kỹ năng|\bexperience\b/iu.test(candidate) ||
      /^(?:có |biết |nắm |hiểu |thành thạo|được |hỗ trợ|cơ hội|là sv|sv |sinh viên|đã |tiếng |đi làm|must |proficien|familiar)/iu.test(candidate)) return false;
  if (/[?？]\s*$/u.test(line) || /^(?:what|why|how|when)\b/iu.test(line)) return false;
  if (/chúng (?:mình|tôi)|các bạn|tham gia|phát triển|training|đào tạo|có kinh nghiệm|nhờ|review cv|thảo luận|so sánh|bàn về|\bdiscussion\b|\bhow to\b/iu.test(line)) return false;
  if (/tuyển(?: dụng)?\s*[:：]/iu.test(line) && technologies(line).some(t => ['.NET', 'C#', 'ASP.NET'].includes(t))) return true;
  if (!roles.test(line) || line.length > 180 || /^(?:yêu cầu|requirements?|skills?|kinh nghiệm|có |biết |sử dụng|thành thạo|ưu tiên|nắm |công nghệ|backend:|frontend:)/iu.test(line)) return false;
  return /^\s*(?:\d+[.)]|[-•▪✅👉])\s*/u.test(line) ||
    /developer|engineer|\b(?:pqa|qa|tester|flutter)\b|lập trình|nghiên cứu|bảo mật/iu.test(line);
}

/** One post remains one job. A vacancy never borrows another vacancy's stack or requirements. */
export function extractVacancies(body, { sourceUrl = null, publishedAt = null } = {}) {
  const text = clean(body);
  const signals = signalText(text);
  const lines = text.replace(/\.\s+(?=(?:Hà Nội|Hanoi|HCM|Hồ Chí Minh)\s)/gu, '\n').split('\n').map(line => line.trim()).filter(Boolean);
  const seeker = /(?:em|mình|tôi|i(?:\s+am|'m)?)\s+(?:(?:đang|cần)\s+)?(?:tìm\s*(?:việc|job)|looking for (?:a )?job|seeking (?:a )?job)|nhờ\s*(?:review|đánh giá)\s*cv/iu.test(text);
  const explicitRecruitment = /tuyển|\bhiring\b|recruitment|vacanc(?:y|ies)|tìm\s+(?:intern|fresher|junior)\s+(?:lập trình|developer)/iu.test(signals);
  let headings = lines.map((line, index) => isHeading(line) ? index : -1).filter(index => index >= 0);
  const numbered = headings.filter(index => /^\d+[.)]/u.test(lines[index]));
  const subheadings = headings.filter(index => /^##\s/u.test(lines[index]));
  const initialHeadings = [...headings];
  // Explicit role lists supersede their introductory catalogue/level headline.
  if (numbered.length > 1) headings = numbered;
  else if (subheadings.length > 1) headings = subheadings;
  else headings = headings.filter((start, index) => {
    const next = headings[index + 1];
    if (next === undefined) return true;
    const between = lines.slice(start + 1, next).join('\n');
    const sameFamily = /^>\s/u.test(lines[next]) && /developer/iu.test(lines[start]) && /developer/iu.test(lines[next]) &&
      JSON.stringify(technologies(lines[start])) === JSON.stringify(technologies(lines[next])) &&
      JSON.stringify((lines[start].match(entry) ?? []).map(v => v.toLowerCase())) === JSON.stringify((lines[next].match(entry) ?? []).map(v => v.toLowerCase()));
    return !(sameFamily && !technologies(between).length && !/yêu cầu|requirements|kinh nghiệm|salary|lương/iu.test(between));
  });
  const intro = lines.slice(0, headings[0] ?? lines.length).join('\n');
  const allLocation = location(text);
  const commonContacts = contacts(text);
  const introContacts = contacts(intro);
  const employer = employerMention(text);
  const employerEvidence = employer.value;
  const commonSeniority = unique(signalText(intro).match(entry) ?? []);
  // Generic parent headings (e.g. Fresher Lập trình viên) supply level, not stack.
  const parentLevels = initialHeadings.length > 1 && !technologies(lines[initialHeadings[0]]).some(t => ['.NET', 'C#', 'ASP.NET', 'ASP.NET Core', 'Flutter', 'Python', 'Java'].includes(t))
    ? unique(lines[initialHeadings[0]].match(entry) ?? []) : [];
  const starts = headings.length ? headings : [0];
  // On multi-role posts, generic closing sections remain post-owned. They do not
  // become technical requirements/contact details of whichever role was last.
  const lastStart = starts.at(-1);
  const headingOnlyList = starts.length > 1 && starts.slice(0, -1).every((start, index) => starts[index + 1] === start + 1);
  const sharedStart = starts.length > 1 ? lines.findIndex((line, index) => index > lastStart &&
    (footerLabel.test(labelText(line)) || benefitsLabel.test(labelText(line)) ||
      headingOnlyList && requirementsLabel.test(labelText(line)) ||
      /^yêu cầu (?:chung|đối với)|^shared requirements/iu.test(labelText(line)) ||
      /^\d+\s/u.test(labelText(line)) && location(line).city)) : -1;
  const sharedLines = sharedStart >= 0 ? lines.slice(sharedStart) : [];
  const sharedRequirements = extractRequirements([...intro.split('\n'), ...sharedLines]);
  let sectionLocation = null;
  const precedingLocations = lines.map(line => {
    const previous = sectionLocation;
    if (allLocation.ambiguous) {
      const section = location(line);
      if (section.city) sectionLocation = section;
    }
    return previous;
  });
  const vacancies = starts.map((start, index) => {
    let blockLines = lines.slice(start, starts[index + 1] ?? (sharedStart >= 0 ? sharedStart : lines.length));
    const nextSection = blockLines.findIndex((line, i) => i > 0 && /khu vực|\b(?:hcm|hanoi)\s*[:：]/iu.test(line) && location(line).city);
    if (allLocation.ambiguous && nextSection > 0) blockLines = blockLines.slice(0, nextSection);
    // A shared address/contact footer must not become the last role's salary/requirements.
    const footer = blockLines.findIndex((line, i) => i > 0 && location(line).city && /^(?:\d+\s|địa chỉ|địa điểm|address|office)/iu.test(line));
    if (starts.length > 1 && index === starts.length - 1 && !allLocation.ambiguous && footer > 0) blockLines = blockLines.slice(0, footer);
    const block = blockLines.join('\n');
    const ownLocation = location(block);
    const precedingLocation = precedingLocations[start];
    const conflictingSection = ownLocation.city && precedingLocation?.city && ownLocation.city !== precedingLocation.city && !location(lines[start]).city;
    const assigned = ownLocation.ambiguous || conflictingSection ? { ...ownLocation, city: null, value: null, ambiguous: true }
      : ownLocation.city ? ownLocation : allLocation.ambiguous ? precedingLocation ?? ownLocation : allLocation;
    const title = headings.length ? lines[start].replace(/^\s*(?:\d+[.)]|[-•▪✅👉])\s*/u, '') : null;
    const requirements = extractRequirements(blockLines.slice(headings.length ? 1 : 0));
    const backendResponsibilities=responsibilities(blockLines);
    const tech = technologies(block);
    const titleTechnologies = technologies(title ?? '');
    const mandatoryText = mandatorySkills(requirements ?? '');
    const mandatoryTechnologies = technologies(mandatoryText);
    const backendTechnology = value => ['.NET', 'C#', 'ASP.NET', 'ASP.NET Core'].includes(value);
    const primaryTechnologies = unique([...titleTechnologies, ...mandatoryTechnologies, ...technologies(mandatorySkills(backendResponsibilities??''))]);
    const levels = unique(signalText(title ?? block).match(seniority) ?? (starts.length === 1 ? signalText(block).match(seniority) ?? commonSeniority : parentLevels));
    const hasEntryLevel = levels.some(level => /^(?:intern(?:ship)?|fresher|junior|entry[- ]level|thực tập)$/iu.test(level));
    const exp = experience(mandatoryText);
    // For compact provider posts without a title, retain explicit employer/stack
    // signals, but never upgrade an optional mention or a benefit/training stack.
    const untitledSkills = !title ? technologies(mandatorySkills(signalText(block))) : [];
    const competingTitle = titleTechnologies.some(t => ['Java', 'React', 'Flutter', 'Python', 'C/C++'].includes(t));
    const backend = titleTechnologies.some(backendTechnology) ||
      (!competingTitle || mandatoryTechnologies.some(t => ['.NET', 'ASP.NET', 'ASP.NET Core'].includes(t))) &&
      (mandatoryTechnologies.some(backendTechnology) || mandatoryTechnologies.includes('EF Core') && roles.test(title ?? '') || untitledSkills.some(backendTechnology));
    const recruitment = !seeker && (explicitRecruitment || title !== null && (levels.length > 0 || starts.length > 1 || /apply|ứng tuyển|requirements|yêu cầu|inbox|salary|lương/iu.test(block)));
    let classification = 'NOT_MATCHED'; let reason = 'TECHNOLOGY_MISMATCH';
    if (seeker) reason = 'CANDIDATE_SEEKING_WORK';
    else if (!recruitment) reason = 'RECRUITMENT_UNVERIFIED';
    else if (/\b(?:pqa|qa|tester)\b|data label|bảo mật|an ninh/iu.test(title ?? '')) reason = 'ROLE_MISMATCH';
    else if (backend) {
      if (assigned.city === 'HCM') reason = 'LOCATION_MISMATCH';
      else if (exp.minimum > 2 || !hasEntryLevel && /\bmiddle\b|\bsenior\b|\blead\b/iu.test(title ?? '')) reason = 'SENIORITY_MISMATCH';
      else if (assigned.city !== 'HANOI') { classification = 'INSUFFICIENT_DATA'; reason = 'LOCATION_UNVERIFIED'; }
      else if (hasEntryLevel) { classification = 'TARGET_MATCH'; reason = 'EXPLICIT_ENTRY_LEVEL'; }
      else if (exp.minimum !== null && exp.minimum <= 2) { classification = 'POTENTIAL_MATCH'; reason = 'COMPATIBLE_EXPERIENCE'; }
      else { classification = 'INSUFFICIENT_DATA'; reason = 'SENIORITY_UNVERIFIED'; }
    } else if (recruitment && !tech.length && levels.length) { classification = 'INSUFFICIENT_DATA'; reason = 'TECHNOLOGY_UNVERIFIED'; }
    const ownContacts = contacts(block);
    const primaryRole=/full[- ]?stack/iu.test(title??'')?'FULLSTACK':/back[- ]?end/iu.test(title??'')?'BACKEND':/front[- ]?end/iu.test(title??'')?'FRONTEND':null;
    const application=applicationDetails(block);
    const recruitmentStatus=/đóng tuyển|đã tuyển đủ|no longer accepting|(?:recruitment|position|applications?) (?:is |are )?closed/iu.test(block)?'CLOSED':recruitment?'ANNOUNCED_UNVERIFIED':'UNKNOWN';
    return { title, employer: employerEvidence, employerStatus: employer.status, location: assigned.value, technologies: tech, primaryTechnologies, seniority: levels,
      primaryRole,requiredTechnologies:primaryTechnologies,optionalTechnologies:technologies((requirements??'').split(/\n|;/u).filter(line=>optionalSkill.test(line)).join('\n')),
      backendResponsibilities,...application,recruitmentStatus,
      missingFields:Object.entries({title,employer:employerEvidence,location:assigned.value,requirements,backendResponsibilities,
        publishedAt,...application}).filter(([,value])=>value===null).map(([key])=>key),
      extractionConfidence:'EVIDENCE_ONLY_NOT_INDEPENDENTLY_VERIFIED',
      experience: exp, requirements, salary: salary(block, 'VACANCY_RANGE_NOT_LEVEL_SPECIFIC'),
      // Only explicit introductory contacts are shared. A different role's contact is not inherited.
      contactEmail: ownContacts.contactEmail ?? introContacts.contactEmail,
      contactPhone: ownContacts.contactPhone ?? introContacts.contactPhone,
      contactMethod: ownContacts.contactMethod ?? introContacts.contactMethod,
      sourceUrl, publishedAt, match: { classification, reason, backend, angular: tech.includes('Angular'),
        locationVerified: assigned.city === 'HANOI', locationAssignmentAmbiguous: !assigned.city && allLocation.ambiguous,
        freshness: publishedAt ? 'UNVERIFIED' : 'UNKNOWN', eligibleForProductionIngestion: false },
      evidence: { title, employer: employer.evidence, location: assigned.evidence, requirements,backendResponsibilities, body: block } };
  });
  if (!text) vacancies.length = 0;
  const classification = !text ? 'INSUFFICIENT_DATA' : ['TARGET_MATCH', 'POTENTIAL_MATCH', 'INSUFFICIENT_DATA'].find(value => vacancies.some(v => v.match.classification === value)) ?? 'NOT_MATCHED';
  const single = vacancies.length === 1 ? vacancies[0] : null;
  return { extractionVersion: EXTRACTION_VERSION, title: single?.title ?? null, employer: employerEvidence, employerStatus: employer.status,
    location: single?.location ?? (!allLocation.ambiguous ? allLocation.value : null), requirements: single?.requirements ?? sharedRequirements,
    sharedRequirements: starts.length > 1 ? sharedRequirements : null,
    postSections: { introduction: intro || null, sharedFooter: sharedLines.join('\n') || null },
    sharedApplicationDetails:applicationDetails([...intro.split('\n'),...sharedLines].join('\n')),
    technologies: unique(vacancies.flatMap(v => v.technologies)), seniority: single?.seniority ?? [],
    salary: single?.salary ?? salary([...intro.split('\n'), ...sharedLines].join('\n'), 'POST_UNASSIGNED'), ...commonContacts, vacancies,
    structuredMatch: { classification, recruitment: !seeker && (explicitRecruitment || headings.length > 0),
      locationAssignmentAmbiguous: vacancies.some(v => v.match.locationAssignmentAmbiguous), freshness: publishedAt ? 'UNVERIFIED' : 'UNKNOWN',
      evidenceTerms: { technology: unique(vacancies.flatMap(v => v.technologies)), seniority: unique(vacancies.flatMap(v => v.seniority)),
        location: allLocation.city === 'HANOI' ? ['HANOI'] : [], recruitment: !seeker && (explicitRecruitment || headings.length) ? ['EMPLOYER_ROLE_EVIDENCE'] : [] },
      eligibleForProductionIngestion: false } };
}

export function structuredMetrics(jobs) {
  const vacancies = jobs.flatMap(job => job.vacancies ?? []);
  const relevant = vacancy => ['TARGET_MATCH', 'POTENTIAL_MATCH'].includes(vacancy.match.classification);
  return { relevantVacancies: vacancies.filter(relevant).length,
    relevantSourcePosts: jobs.filter(job => job.vacancies?.some(relevant)).length,
    potentialMatches: vacancies.filter(v => v.match.classification === 'POTENTIAL_MATCH').length,
    unmatchedPosts: jobs.filter(job => job.match.classification === 'NOT_MATCHED').length,
    emailCoverage: { withEmail: jobs.filter(job => job.contactEmail).length, totalPosts: jobs.length },
    structuredExtractionCoverage: { withVacancies: jobs.filter(job => job.vacancies?.length).length,
      withTitle: jobs.filter(job => job.vacancies?.some(v => v.title)).length,
      withLocation: jobs.filter(job => job.vacancies?.some(v => v.location)).length,
      withRequirements: jobs.filter(job => job.vacancies?.some(v => v.requirements)).length, totalPosts: jobs.length },
    unknownFreshness: jobs.filter(job => job.match.freshness === 'UNKNOWN').length };
}

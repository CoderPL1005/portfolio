import { createHash } from 'node:crypto';
import { Stop, JobPostExtractor } from './core.mjs';
import { canonicalFacebookPost } from './discovery-contract.mjs';
import { normalizeSocialApisItem } from './socialapis-live-smoke.mjs';
import { classifyBackendProfile } from './backend-matching.mjs';
import { classifyCandidateProfile } from './profile-matching.mjs';

export const REVIEW_VERSION = 'content-first-v1';
const hash = text => createHash('sha256').update(text, 'utf8').digest('hex');
const notes = {
  EMPLOYER_HIRING: 'Có nội dung nhà tuyển dụng đang tìm ứng viên; chưa xác minh công ty.',
  CANDIDATE_SEEKING_WORK: 'Người viết đang tìm việc, không phải nhà tuyển dụng đăng vị trí.',
  NON_RECRUITMENT_CONTENT: 'Nội dung chia sẻ, hỏi đáp hoặc quảng cáo; chưa có cơ hội tuyển dụng rõ ràng.',
  RECRUITMENT_CLOSED: 'Bài viết cho biết đã đóng tuyển hoặc ngừng nhận hồ sơ.',
  CONTRADICTORY_CONTENT: 'Các tín hiệu tuyển dụng mâu thuẫn, cần người dùng kiểm tra.',
  RECRUITMENT_UNCLEAR: 'Chưa đủ thông tin để kết luận đây là thông báo tuyển dụng.',
  ITEM_NOT_OBJECT: 'Item không phải object hợp lệ.', POST_TEXT_MISSING: 'Không có nội dung bài viết để đánh giá.',
  POST_TEXT_TOO_LARGE: 'Nội dung vượt giới hạn an toàn; không lưu payload.',
  POST_TEXT_UNSAFE: 'Nội dung chứa ký tự NUL không an toàn; chỉ lưu outcome để review.',
  PROFILE_NOT_PROVIDED: 'Chưa có Technology Profile để đối chiếu kỹ năng.',
  TARGET_MATCH: 'Vị trí phù hợp với hồ sơ và mục tiêu đã cung cấp.',
  NOT_MATCHED: 'Vị trí không đáp ứng mục tiêu; điều này không đồng nghĩa bài tuyển dụng không hợp lệ.',
  FALLBACK_MATCH: 'Cơ hội dự phòng theo chính sách kinh nghiệm; không tính là primary yield.',
  POTENTIAL_MATCH: 'Có tiềm năng nhưng cần kiểm tra thêm bằng chứng kỹ năng hoặc stack.',
  INSUFFICIENT_DATA: 'Thiếu thông tin quan trọng để xác định độ phù hợp.'
};
const targetNotes = {
  INCOMPATIBLE_PRIMARY_STACK: 'Stack chính của vị trí không phù hợp với các backend stack trong mục tiêu.',
  FRONTEND_ONLY: 'Vị trí chỉ làm frontend, không thuộc mục tiêu backend.',
  SENIORITY_MISMATCH: 'Cấp độ tuyển dụng không thuộc Intern/Fresher đã chọn.',
  REQUIRED_EXPERIENCE_EXCEEDED: 'Số năm kinh nghiệm bắt buộc vượt giới hạn mục tiêu.',
  FALLBACK_REQUIRED_EXPERIENCE: 'Yêu cầu hai năm chỉ được xem là cơ hội dự phòng.',
  LOCATION_UNVERIFIED_OR_MISMATCH: 'Địa điểm thiếu, mơ hồ hoặc không thuộc khu vực mục tiêu.',
  DEVELOPING_TECHNOLOGY_ONLY: 'Chỉ có bằng chứng đang học, chưa có bằng chứng sử dụng phù hợp.',
  STACK_EVIDENCE_INCOMPLETE: 'Chưa đủ bằng chứng Portfolio cho stack bắt buộc.',
  BACKEND_STACK_UNVERIFIED: 'Vị trí backend chưa nêu rõ stack để đối chiếu.',
  BACKEND_ROLE_UNVERIFIED: 'Chưa xác định được trách nhiệm backend từ nội dung.',
  FULLSTACK_BACKEND_UNVERIFIED: 'Full-stack chưa có bằng chứng phần backend đáng kể.',
  SENIORITY_UNVERIFIED: 'Chưa xác định cấp độ tuyển dụng.',
  REQUIRED_EXPERIENCE_CONFLICT: 'Các yêu cầu kinh nghiệm mâu thuẫn nhau.',
  DEMONSTRATED_BACKEND_ENTRY_ROLE: 'Backend Intern/Fresher phù hợp, có bằng chứng sử dụng stack trong Portfolio.'
};
const decision = (status, reason, snippets = []) => ({ status, reasonCodes: [reason], explanation: notes[reason] ?? notes[status], evidenceSnippets: snippets });
const object = row => row !== null && typeof row === 'object' && !Array.isArray(row);

// A review observation is NOT an accepted Facebook identity. Never feed this
// collection into the legacy registry/ledger or infer aliases from content.
export function buildReviewItems(rows, context = {}, budget) {
  const evaluatedAt = context.evaluatedAt ?? new Date().toISOString();
  const seen = new Set();
  return rows.map((row, itemIndex) => {
    budget?.check();
    const fields = object(row) ? (Object.hasOwn(row, 'basic_info') ? row.basic_info : row) : null;
    const normalized = normalizeSocialApisItem(row);
    const originalText = normalized?.rawContent;
    const invalid = !object(row) || !object(fields) ? 'ITEM_NOT_OBJECT'
      : typeof originalText !== 'string' || !originalText.trim() ? 'POST_TEXT_MISSING'
      : originalText.length > 24_000 ? 'POST_TEXT_TOO_LARGE' : originalText.includes('\0') ? 'POST_TEXT_UNSAFE' : null;
    const rawContent = invalid ? null : originalText;
    const rawUrl = object(fields) ? (Object.hasOwn(row, 'basic_info') ? fields.url : fields.permalink ?? fields.url ?? fields.post_url ?? fields.post_link) : null;
    const sourceUrl = canonicalFacebookPost(rawUrl)?.url ?? null;
    const sourceWarnings = [];
    if (normalized?.idUrlConflict) sourceWarnings.push('POST_ID_URL_MISMATCH');
    const providerPostId = normalized?.sourceExternalId && /^(?:\d{1,100}|pfbid[A-Za-z0-9]{1,200})$/u.test(normalized.sourceExternalId) ? normalized.sourceExternalId : null;
    if (!providerPostId) sourceWarnings.push('POST_ID_MISSING_OR_INVALID');
    if (!sourceUrl) sourceWarnings.push(rawUrl ? 'POST_URL_INVALID_OR_UNSUPPORTED' : 'POST_URL_MISSING');
    const contentHash = rawContent === null ? null : hash(rawContent);
    const contentGroupHash = rawContent === null ? null : hash(rawContent.normalize('NFC').replace(/\r\n?/gu, '\n').trim());
    if (contentGroupHash && seen.has(contentGroupHash)) sourceWarnings.push('DUPLICATE_CONTENT');
    if (contentGroupHash) seen.add(contentGroupHash);
    let contentValidity = decision('NEEDS_REVIEW', invalid ?? 'RECRUITMENT_UNCLEAR');
    let targetMatching = decision('INSUFFICIENT_DATA', invalid ?? 'PROFILE_NOT_PROVIDED');
    let extracted = { company: null, role: null, location: null, technologies: [], experience: null, salary: null, contact: null, vacancies: [] };
    const missingFields = [];
    if (rawContent !== null) {
      const lines = rawContent.split(/\r?\n/u).filter(Boolean);
      const evidence = pattern => lines.filter(line => pattern.test(line)).slice(0, 3).map(line => line.slice(0, 500));
      const seeking = /(?:em|mình|tôi|i(?: am|'m)?)\s+(?:(?:đang|cần)\s+)?(?:tìm\s*(?:việc|job)|looking for (?:a )?job|seeking (?:a )?job)/iu;
      const hiring = /tuyển(?: dụng)?(?!\s+sinh)|\bhiring\b|\brecruitment\b|\bvacanc(?:y|ies)\b|tìm (?:fresher|intern|developer)/iu;
      const closed = /đã (?:đóng tuyển|tuyển đủ|ngừng tuyển)|đóng tuyển|ngừng nhận (?:cv|hồ sơ)|(?:position|vacancy|applications?|recruitment) (?:is |are )?closed|no longer accepting/iu;
      const nonJob = /khóa học|đăng ký học|tuyển sinh|course|chia sẻ kiến thức|hỏi đáp|tutorial|how to|review cv/iu;
      const questionPattern = /^\s*(?:có (?:công ty|ai|đơn vị)|ai biết|cho (?:em|mình) hỏi|where|how|what)[^\n]*\?/iu;
      const question = questionPattern.test(rawContent);
      const title = /developer|engineer|lập trình|tester|\bpqa\b|nhân viên/iu;
      const seeker = seeking.test(rawContent), employer = hiring.test(rawContent) && title.test(rawContent);
      const reason = seeker && employer ? 'CONTRADICTORY_CONTENT' : seeker ? 'CANDIDATE_SEEKING_WORK'
        : closed.test(rawContent) ? 'RECRUITMENT_CLOSED' : question ? 'NON_RECRUITMENT_CONTENT' : employer ? 'EMPLOYER_HIRING'
        : nonJob.test(rawContent) ? 'NON_RECRUITMENT_CONTENT' : 'RECRUITMENT_UNCLEAR';
      contentValidity = decision(reason === 'EMPLOYER_HIRING' ? 'VALID_RECRUITMENT'
        : ['CONTRADICTORY_CONTENT', 'RECRUITMENT_UNCLEAR'].includes(reason) ? 'NEEDS_REVIEW' : 'INVALID_RECRUITMENT', reason,
      evidence(reason === 'CANDIDATE_SEEKING_WORK' ? seeking : reason === 'RECRUITMENT_CLOSED' ? closed : reason === 'NON_RECRUITMENT_CONTENT' ? question ? questionPattern : nonJob : hiring));
      let job = new JobPostExtractor().extract({ id: normalized?.sourceExternalId ?? `review-${itemIndex}`, provider: 'SOCIALAPIS',
        source: 'FACEBOOK', sourceUrl, title: '', employer: '', location: '', requirements: '', body: rawContent,
        publishedAt: normalized?.publishedAt ?? null, hasImage: false });
      if (context.profile) {
        job = context.targetJobPreferences ? classifyBackendProfile(job, context.profile, context.targetJobPreferences)
          : classifyCandidateProfile(job, context.profile);
        const status = contentValidity.status === 'INVALID_RECRUITMENT' ? 'NOT_MATCHED' : job.structuredMatch.classification;
        targetMatching = decision(status, status, job.vacancies.flatMap(v => [v.evidence.title, v.evidence.requirements]).filter(s => s && rawContent.includes(s)).slice(0, 3).map(s => s.slice(0, 500)));
        targetMatching.reasonCodes = [...new Set(job.vacancies.map(v => v.match.reason))];
        targetMatching.explanation = [notes[status], ...targetMatching.reasonCodes.map(code => targetNotes[code] ?? notes[code]).filter(Boolean)].join(' ');
      }
      // Explicit allowlist: no author object, provider metadata, cursor or tokens.
      extracted = { company: job.employer ?? null, role: job.title ?? null, location: job.location ?? null,
        technologies: job.technologies, experience: job.vacancies.length === 1 ? job.vacancies[0].experience : null,
        salary: job.salary ?? null, contact: { email: job.contactEmail ?? null, phone: job.contactPhone ?? null, method: job.contactMethod ?? null },
        vacancies: job.vacancies.map(v => ({ title: v.title, location: v.location, technologies: v.technologies,
          experience: v.experience, salary: v.salary, requirements: v.requirements,
          targetStatus: v.match.classification, reason: v.match.reason })) };
      if (!extracted.company) missingFields.push('COMPANY');
      if (!extracted.role && !extracted.vacancies.some(v => v.title)) missingFields.push('ROLE');
      if (!extracted.location) missingFields.push('LOCATION');
      if (job.structuredMatch.locationAssignmentAmbiguous) missingFields.push('AMBIGUOUS_LOCATION');
      if (!job.employer && job.vacancies.some(v => v.evidence.employer.length > 1)) missingFields.push('AMBIGUOUS_COMPANY');
    }
    if (!normalized?.publishedAt) missingFields.push('PUBLISHED_AT');
    return { itemIndex, providerPostId, sourceUrl, rawContent, contentHash, contentGroupHash,
      publishedAt: normalized?.publishedAt ?? null, contentValidity, targetMatching, verificationStatus: 'NOT_CHECKED',
      extracted, missingFields, sourceWarnings, classifierVersion: REVIEW_VERSION, evaluatedAt,
      profileFingerprint: context.profile ? hash(JSON.stringify(context.profile)) : null,
      preferencesFingerprint: context.targetJobPreferences ? hash(JSON.stringify(context.targetJobPreferences)) : null };
  });
}

export function reviewExport(items, { runId, query, pageNumber = 1 }) {
  if (typeof runId !== 'string' || !/^[A-Za-z0-9._-]{1,100}$/u.test(runId) || !Number.isInteger(pageNumber) || pageNumber < 1 || pageNumber > 6) throw new Stop('INVALID_REVIEW_COORDINATES');
  return { schemaVersion: 1, provider: 'SOCIALAPIS', runId, query, pageNumber, items };
}

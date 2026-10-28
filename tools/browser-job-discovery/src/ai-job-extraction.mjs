import { createHash } from 'node:crypto';
import { EXTRACTION_SCHEMA_VERSION, PROMPT_VERSION, EXTRACTION_INSTRUCTIONS, extractionJsonSchema, validateExtractionShape } from './ai-extraction-schema.mjs';
import { validateExtractionEvidence } from './ai-extraction-evidence.mjs';

const hash = s => createHash('sha256').update(s, 'utf8').digest('hex');
const bounded = (x, fallback, maximum) => {
  const n = x ?? fallback;
  if (!Number.isSafeInteger(n) || n < 1 || n > maximum) throw Error('AI_EXTRACTION_LIMIT_INVALID');
  return n;
};
function prepareInput(input, maxInputChars) {
  if (!input || typeof input !== 'object' || Array.isArray(input) || typeof input.rawContent !== 'string' || !input.rawContent.trim() ||
    input.rawContent.includes('\0')) throw Error('AI_EXTRACTION_INPUT_INVALID');
  if (input.rawContent.length > maxInputChars) throw Error('AI_EXTRACTION_INPUT_TOO_LARGE');
  if (typeof input.sourcePostId !== 'string' || !/^[A-Za-z0-9._:-]{1,200}$/u.test(input.sourcePostId) || input.contentHash !== hash(input.rawContent))
    throw Error('AI_EXTRACTION_INPUT_IDENTITY_OR_HASH_INVALID');
  if (input.sourceUrl !== null) {
    if (typeof input.sourceUrl !== 'string') throw Error('AI_EXTRACTION_INPUT_URL_INVALID');
    try { const u = new URL(input.sourceUrl); if (!['https:', 'http:'].includes(u.protocol) || u.username || u.password || input.sourceUrl.length > 2048 || /\s/u.test(input.sourceUrl)) throw Error(); }
    catch { throw Error('AI_EXTRACTION_INPUT_URL_INVALID'); }
  }
  if (input.publishedAt !== null && (typeof input.publishedAt !== 'string' || !/^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z)?$/u.test(input.publishedAt) ||
    !Number.isFinite(Date.parse(input.publishedAt)) || new Date(input.publishedAt).toISOString().slice(0, 10) !== input.publishedAt.slice(0, 10)))
    throw Error('AI_EXTRACTION_INPUT_PUBLICATION_INVALID');
  // Explicit allowlist, fresh object. No spread of observation/provider metadata.
  return { sourcePostId: input.sourcePostId, sourceUrl: input.sourceUrl, rawContent: input.rawContent,
    contentHash: input.contentHash, publishedAt: input.publishedAt };
}

/** Provider-neutral boundary: provider.extract(request, {signal}) -> JSON string.
 * No HTTP/SDK/default provider, retries, file writes or global cache here.
 * Future adapters must honor signal and implement their own authorized transport.
 */
export async function extractRecruitmentPost(input, options = {}) {
  const maxInputChars = bounded(options.maxInputChars, 24000, 24000);
  const maxOutputBytes = bounded(options.maxOutputBytes, 65536, 65536);
  const timeoutMs = bounded(options.timeoutMs, 20000, 20000);
  const prepared = prepareInput(input, maxInputChars);
  const { provider } = options, promptVersion = options.promptVersion ?? PROMPT_VERSION, schemaVersion = options.schemaVersion ?? EXTRACTION_SCHEMA_VERSION;
  if (!provider || typeof provider.extract !== 'function' || typeof provider.modelIdentity !== 'string' || !/^[A-Za-z0-9._:/-]{1,160}$/u.test(provider.modelIdentity))
    throw Error('AI_EXTRACTION_PROVIDER_REQUIRED');
  if (schemaVersion !== EXTRACTION_SCHEMA_VERSION || typeof promptVersion !== 'string' || !/^ai-extraction-prompt-v[1-9]\d*$/u.test(promptVersion))
    throw Error('AI_EXTRACTION_VERSION_UNSUPPORTED');
  const cacheKey = hash(JSON.stringify([prepared.contentHash, 'ai-job-extraction-v1', schemaVersion, promptVersion, provider.modelIdentity, hash(EXTRACTION_INSTRUCTIONS)]));
  // The adapter owns its request copy, never the trusted validation snapshot.
  const request = { input: structuredClone(prepared), promptVersion, schemaVersion, modelIdentity: provider.modelIdentity,
    instructions: EXTRACTION_INSTRUCTIONS, responseSchema: extractionJsonSchema(), limits: { timeoutMs, maxOutputBytes } };
  const controller = new AbortController(); let timer;
  let raw;
  try {
    raw = await Promise.race([
      Promise.resolve().then(() => provider.extract(request, { signal: controller.signal })),
      new Promise((_, reject) => { timer = setTimeout(() => { reject(Error('AI_EXTRACTION_TIMEOUT')); controller.abort(); }, timeoutMs); })
    ]);
  } catch (e) {
    // Never expose provider exception messages, tokens, post text or raw output.
    if (e?.message === 'AI_EXTRACTION_TIMEOUT' && controller.signal.aborted) throw Error('AI_EXTRACTION_TIMEOUT');
    throw Error('AI_EXTRACTION_PROVIDER_FAILED');
  } finally { clearTimeout(timer); }
  if (typeof raw !== 'string') throw Error('AI_EXTRACTION_OUTPUT_SCHEMA_INVALID');
  if (Buffer.byteLength(raw, 'utf8') > maxOutputBytes) throw Error('AI_EXTRACTION_OUTPUT_TOO_LARGE');
  let parsed;
  try { parsed = JSON.parse(raw); } catch { throw Error('AI_EXTRACTION_OUTPUT_JSON_INVALID'); }
  validateExtractionShape(parsed);
  const normalized = validateExtractionEvidence(parsed, prepared.rawContent);
  return { ...normalized, sourcePostId: prepared.sourcePostId, sourceUrl: prepared.sourceUrl, contentHash: prepared.contentHash,
    extractionVersion: 'ai-job-extraction-v1', promptVersion, modelIdentity: provider.modelIdentity, cacheKey,
    verificationStatus: 'NOT_CHECKED', publicationEvidenceStatus: normalized.publishedAt.value === null ? 'UNKNOWN' : 'POST_MENTIONED',
    extractionStatus: normalized.diagnostics.length ? 'VALIDATED_WITH_DIAGNOSTICS' : 'VALIDATED' };
}

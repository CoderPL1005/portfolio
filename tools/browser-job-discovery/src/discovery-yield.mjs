// Current-run accounting only. Never infer publication time from request filters
// or registry membership; this function does not mutate extracted jobs/state.
export function buildDiscoveryYield({ jobs, existingJobs = [], query, itemStats, requestStats,
  dateFilters = { startTime: null, endTime: null }, mode = 'REPLAY', now = new Date() }) {
  const existing = new Set(existingJobs.map(job => job.sourceIdentity));
  const relevant = vacancy => ['TARGET_MATCH', 'POTENTIAL_MATCH'].includes(vacancy.match?.classification);
  const relevantVacancies = jobs.flatMap(job => (job.vacancies ?? []).filter(relevant).map(vacancy => ({ job, vacancy })));
  const newVacancies = relevantVacancies.filter(({ job }) => !existing.has(job.sourceIdentity));
  const relevantPosts = new Set(relevantVacancies.map(({ job }) => job.sourceIdentity));
  const acceptedItems = itemStats?.acceptedItems ?? jobs.length;
  const withinResponseDuplicates = Math.max(0, acceptedItems - jobs.length);
  const existingSourcePosts = jobs.filter(job => existing.has(job.sourceIdentity)).length;
  const credits = requestStats?.creditsDeducted ?? null;
  const perCredit = count => typeof credits === 'number' && Number.isFinite(credits) && credits > 0 ? count / credits : null;
  const recent = job => {
    // SocialAPIs currently emits UNKNOWN/UNVERIFIED. A filter or parseable
    // timestamp cannot upgrade these. Reuse only an existing verified freshness.
    const age = now - new Date(job.publishedAt);
    return job.match?.freshness === 'RECENT_7D' && !!job.publishedAt && Number.isFinite(age) && age >= 0 && age <= 7 * 86_400_000;
  };
  const verifiedRecent = relevantVacancies.filter(({ job }) => recent(job)).length;
  const withEmail = relevantVacancies.filter(({ job, vacancy }) => {
    if (vacancy.contactEmail) return true;
    const shared = [job.postSections?.introduction, job.postSections?.sharedFooter].filter(Boolean).join('\n');
    return !!job.contactEmail && (job.vacancies.length === 1 || shared.includes(job.contactEmail));
  }).length;
  return {
    scope: 'CURRENT_RUN', query, requestCount: requestStats?.requestCount ?? 0,
    creditsDeducted: credits, creditsUsedAlternateHeader: requestStats?.creditsUsedAlternateHeader ?? null,
    totalItems: itemStats?.totalItems ?? jobs.length, acceptedItems, rejectedItems: itemStats?.rejectedItems ?? 0,
    newSourcePosts: jobs.length - existingSourcePosts, existingSourcePosts,
    relevantSourcePosts: relevantPosts.size,
    newRelevantSourcePosts: [...relevantPosts].filter(identity => !existing.has(identity)).length,
    relevantVacancies: relevantVacancies.length, newRelevantVacancies: newVacancies.length,
    duplicateRate: acceptedItems > 0 ? (existingSourcePosts + withinResponseDuplicates) / acceptedItems : null,
    freshnessUnknownCount: jobs.filter(job => !job.publishedAt || job.match?.freshness === 'UNKNOWN').length,
    verifiedRecentRelevantVacancies: verifiedRecent, relevantVacanciesWithContactEmail: withEmail,
    relevantVacanciesPerCredit: perCredit(relevantVacancies.length), newRelevantVacanciesPerCredit: perCredit(newVacancies.length),
    verifiedRecentVacanciesPerCredit: perCredit(verifiedRecent),
    requestMetadata: { mode, startTime: dateFilters.startTime, endTime: dateFilters.endTime,
      dateFiltersRequested: !!(dateFilters.startTime || dateFilters.endTime),
      providerSearchPerformed: mode === 'LIVE_PROVIDER_RESPONSE' && (requestStats?.requestCount ?? 0) > 0,
      providerDateFilterBehavior: 'UNVERIFIED',
      publicationDates: { unknown: jobs.filter(job => !job.publishedAt).length,
        timestampPresentUnverified: jobs.filter(job => job.publishedAt && job.match?.freshness === 'UNVERIFIED').length,
        verifiedRecent: jobs.filter(recent).length } }
  };
}

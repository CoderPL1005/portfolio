import { PagedResult } from '../../../core/api/paged-result.model';
export interface ReviewDecision { status: string; reasonCodes: string[]; explanation: string; evidenceSnippets: string[] }
export interface DiscoveryReviewItem {
  itemIndex: number; providerPostId: string | null; sourceUrl: string | null; rawContent: string | null;
  publishedAt: string | null; contentValidity: ReviewDecision; targetMatching: ReviewDecision;
  verificationStatus: 'NOT_CHECKED'; extracted: Record<string, unknown>; missingFields: string[];
  sourceWarnings: string[]; classifierVersion: string; evaluatedAt: string;
  profileFingerprint?:string|null; preferencesFingerprint?:string|null;
}
export interface DiscoveryReviewDetail {
  id: string; runId: string; query: string; pageNumber: number; ingestedAt: string; duplicateCount: number; item: DiscoveryReviewItem;
  humanReview?: HumanReview;
}
export interface HumanReview {
  status: 'UNREVIEWED'|'CONFIRMED'|'CORRECTED'|'UNCERTAIN'; version: number;
  contentStatus: string|null; targetStatus: string|null; notes: string|null;
  reviewedAt: string|null; reviewedByAdminId: string|null;
}
export interface HumanReviewWrite { expectedVersion: number; action: 'CONFIRMED'|'CORRECTED'|'UNCERTAIN'; contentStatus?: string; targetStatus?: string; notes: string }
export interface ReviewHistory extends PagedResult<HumanReview> { current: HumanReview }
export interface QualityCounts { total:number; reviewed:number; unreviewed:number; confirmed:number; corrected:number; uncertain:number }
export interface DimensionQuality {
  eligibleSamples:number; abstentions:number; unreviewed:number; uncertain:number; humanConflicts:number; predictionConflicts:number;
  matrix:{prediction:string;human:string;count:number}[];
  classes:{label:string;tp:number;fp:number;fn:number;support:number;precisionDenominator:number;recallDenominator:number;precision:number|null;recall:number|null}[];
}
export interface QualityLevel { contentValidity:DimensionQuality; targetMatching:DimensionQuality }
export interface QualityCohort { classifierVersion:string;profileFingerprint:string|null;preferencesFingerprint:string|null;counts:QualityCounts;observationLevel:QualityLevel;uniqueContentLevel:QualityLevel }
export interface DiscoveryQualityReport { counts:QualityCounts;observationLevel:QualityLevel;uniqueContentLevel:QualityLevel;cohorts:QualityCohort[];overallMatricesAvailable:boolean;evaluatedAt:string;notice:string }
export interface DiscoveryReviewList {
  results: PagedResult<DiscoveryReviewDetail>;
  counts: { total: number; valid: number; invalid: number; needsReview: number; targetMatch: number; identityWarnings: number; duplicateItems: number };
}

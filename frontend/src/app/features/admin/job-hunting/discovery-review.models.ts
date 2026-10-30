import { PagedResult } from '../../../core/api/paged-result.model';
import { DiscoveryExecutionVacancy } from './discovery-execution.models';
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
  origin?: 'CLASSIFIER'; recovery?: null; originalQuery?:string|null; queryAvailability?:'AVAILABLE';
  observation?: DiscoveryObservationDetail | null;
}
export interface DiscoveryRecoveryEnvelope {
  origin:'PERSISTED_OBSERVATION_RECOVERY';version:'recovery-review-v1';executionId:string;observationId:string;workflowRunId:string;
  operationId:string;receiptId:string;auditId:string;itemIndex:number;sourceFingerprint:string;
  originalQuery:null;originalClassifierResult:null;originalReviewBundle:null;providerPostId:string;
  sourceUrl:string|null;providerSourceUrl:string|null;rawContent:string;contentHash:string;contentGroupHash:string;publishedAt:string|null;
  recoveryDecision:{contentStatus:'NEEDS_REVIEW';targetStatus:'INSUFFICIENT_DATA';verificationStatus:'NOT_CHECKED'};
  sourceWarnings:string[];missingFields:string[];recoveryAt:string;
}
export interface DiscoveryRecoveryDetail {
  id:string;runId:string;query:'';pageNumber:number;ingestedAt:string;duplicateCount:number;
  origin:'PERSISTED_OBSERVATION_RECOVERY';item:null;originalQuery:null;originalClassifierResult:null;queryAvailability:'UNAVAILABLE';
  recovery:DiscoveryRecoveryEnvelope;humanReview?:HumanReview;identityWarning:boolean;
  observation?: DiscoveryObservationDetail | null;
}
export type DiscoveryReviewRecord=DiscoveryReviewDetail|DiscoveryRecoveryDetail;
export interface DiscoveryObservationDetail {
  origin:'EXECUTION_OBSERVATION'; executionId:string; itemIndex:number; workflowRunId:string|null;
  collectedAt:string; executionState:string; semanticState:string; extractionError:string|null;
  source:{itemIndex:number;providerPostId:string|null;sourceUrl:string|null;providerSourceUrl:string|null;rawContent:string|null;contentHash:string|null;publishedAt:string|null;outcome:string|null};
  review:DiscoveryReviewItem|null; vacancies:DiscoveryExecutionVacancy[]; sourceWarnings:string[];
  candidateId:string|null; humanReview:HumanReview|null;
  linkedCandidateIds?:string[]|null;
}
export type DiscoveryOpportunity=DiscoveryReviewRecord|DiscoveryObservationDetail;
export interface HumanReview {
  status: 'UNREVIEWED'|'CONFIRMED'|'CORRECTED'|'UNCERTAIN'|'REVIEWED_RECOVERY'; version: number;
  contentStatus: string|null; targetStatus: string|null; notes: string|null;
  reviewedAt: string|null; reviewedByAdminId: string|null;
}
export interface HumanReviewWrite { expectedVersion: number; action: 'CONFIRMED'|'CORRECTED'|'UNCERTAIN'|'REVIEW_RECOVERY'; contentStatus?: string; targetStatus?: string; notes: string }
export interface ReviewHistory extends PagedResult<HumanReview> { current: HumanReview }
export interface QualityCounts { total:number; reviewed:number; unreviewed:number; confirmed:number; corrected:number; uncertain:number }
export interface DimensionQuality {
  eligibleSamples:number; abstentions:number; unreviewed:number; uncertain:number; humanConflicts:number; predictionConflicts:number;
  matrix:{prediction:string;human:string;count:number}[];
  classes:{label:string;tp:number;fp:number;fn:number;support:number;precisionDenominator:number;recallDenominator:number;precision:number|null;recall:number|null}[];
}
export interface QualityLevel { contentValidity:DimensionQuality; targetMatching:DimensionQuality }
export interface QualityCohort { classifierVersion:string;profileFingerprint:string|null;preferencesFingerprint:string|null;counts:QualityCounts;observationLevel:QualityLevel;uniqueContentLevel:QualityLevel }
export interface DiscoveryQualityReport { counts:QualityCounts;observationLevel:QualityLevel;uniqueContentLevel:QualityLevel;cohorts:QualityCohort[];overallMatricesAvailable:boolean;evaluatedAt:string;notice:string;
  recoveryCoverage?:{origin:string;version:string;total:number;reviewed:number;unreviewed:number;uncertain:number;reviewedRecovery:number;uniqueContents:number;predictionUnavailable:true;precision:null;recall:null};
}
export interface DiscoveryReviewList<T extends DiscoveryOpportunity=DiscoveryReviewDetail> {
  results: PagedResult<T>;
  counts: { total: number; valid: number; invalid: number; needsReview: number; targetMatch: number; identityWarnings: number; duplicateItems: number; reviewed?:number; unknown?:number };
}

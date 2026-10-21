// Ports for the isolated simulator; no production Application/Domain dependency.
export interface IBrowserSessionProvider {
  open(budget: IActivityBudget, signal: AbortSignal): Promise<{
    page: import('playwright').Page;
    close(): Promise<void>;
  }>;
}
export interface ISearchPlanner {
  plan(target: { technologies: string[]; locations: string[]; seniorities: string[] }): string[];
}
export interface IBrowserDiscoveryAdapter {
  search(page: import('playwright').Page | undefined, query: string, budget: IActivityBudget): Promise<string[]>;
  read(page: import('playwright').Page | undefined, candidateUrl: string, budget: IActivityBudget): Promise<AccessiblePost>;
}
export interface IActivityBudget {
  readonly limits: { searches: number; candidates: number; durationMs: number; sessions: number };
  check(): void;
  consume(kind: 'searches' | 'candidates'): void;
  acquireSession(): () => void;
  snapshot(): Record<string, number>;
}
export interface AccessiblePost {
  provider?: 'SOCIALAPIS';
  source?: 'FACEBOOK';
  canonicalSourceUrl?: string;
  published_at?: string | null;
  id: string | null;
  sourceUrl: string;
  kind: string;
  title: string;
  employer: string;
  location: string;
  requirements: string;
  body: string;
  publishedAt: string | null;
  relativeTime: string | null;
  hasImage: boolean;
  evidenceKind: 'SYNTHETIC_FIXTURE' | 'USER_PROVIDED_FIXTURE' | 'LOCAL_PROVIDER_CAPTURE_REPLAY' | 'MOCK_PROVIDER_RESPONSE' | 'LIVE_PROVIDER_RESPONSE';
}
export interface IJobPostExtractor {
  extract(post: AccessiblePost): Record<string, unknown>;
}
export interface IDiscoveryReporter {
  build(input: Record<string, unknown>): Record<string, unknown>;
}

// Discovery-stage output only; never a production ingestion/write DTO.
export interface DiscoveryCandidate {
  source: 'FACEBOOK';
  query: string;
  candidate_post_url: string;
  source_post_id: string;
  retrieved_at: string;
  evidence: Readonly<Record<string, unknown>> & { kind: 'SYNTHETIC_FIXTURE'; live_verified: false };
  authorization_status: 'OFFLINE_FIXTURE_ONLY';
  discovery_strategy: 'FACEBOOK_BROWSER' | 'SOCIALAPISHUB_HTTP';
}
export interface IFacebookBrowserCandidateDiscovery {
  discover(page: import('playwright').Page, query: string, budget: IActivityBudget): Promise<DiscoveryCandidate[]>;
}
export interface IHttpCandidateDiscovery {
  discover(query: string, budget: IActivityBudget): Promise<DiscoveryCandidate[]>;
}

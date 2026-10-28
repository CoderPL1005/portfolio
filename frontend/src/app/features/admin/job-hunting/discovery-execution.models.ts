export interface DiscoveryExecutionVacancy {
  id?: string;
  status?: string;
  jobPostingId?: string | null;
  missingFields?: string[];
  fields?: Record<string, { value: unknown; status: string }>;
}
export interface DiscoveryExecution {
  id: string;
  state: string;
  active?: boolean;
  actualRequests?: number | null;
  creditsDeducted?: number | null;
  errorCode?: string | null;
  createdAt?: string;
  counts?: { fetched?: number; validRecruitment?: number; normalized?: number; duplicates?: number; extractionFailures?: number };
  results?: (DiscoveryExecutionVacancy & {
    itemIndex?: number; staging?: boolean; vacancies?: DiscoveryExecutionVacancy[];
    observation?: { rawContent?: string | null; sourceUrl?: string | null; outcome?: string };
  })[];
}
export interface DiscoveryExecutionHistory { items: DiscoveryExecution[]; page: number; pageSize: number; total: number }

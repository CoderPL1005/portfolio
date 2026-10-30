export interface DiscoveryExecutionVacancy {
  id?: string;
  status?: string;
  jobPostingId?: string | null;
  missingFields?: string[];
  fields?: Record<string, { value: unknown; status: string; origin?:string; conflict?:boolean; evidence?:{start:number;end:number;quote:string}[] }>;
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
    semanticState?: string;
    processing?: { origin: string; review: {contentValidity: {status:string; explanation:string}; targetMatching: {status:string; explanation:string}; sourceWarnings:string[]; missingFields:string[]}|null; vacancies: DiscoveryExecutionVacancy[]; extractionError?:string|null } | null;
    itemIndex?: number; staging?: boolean; vacancies?: DiscoveryExecutionVacancy[];
    observation?: { rawContent?: string | null; sourceUrl?: string | null; providerSourceUrl?:string|null; providerPostId?:string|null; outcome?: string };
  })[];
}
export interface DiscoveryExecutionHistory { items: DiscoveryExecution[]; page: number; pageSize: number; total: number }

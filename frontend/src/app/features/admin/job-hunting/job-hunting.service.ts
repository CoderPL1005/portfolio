import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { PagedResult } from '../../../core/api/paged-result.model';
import { requireData } from '../shared/admin-api';
import { DiscoveryExecution, DiscoveryExecutionHistory } from './discovery-execution.models';
import { DiscoveryReviewRecord, DiscoveryReviewList, HumanReview, HumanReviewWrite, ReviewHistory, DiscoveryQualityReport } from './discovery-review.models';
import {DiscoveryRunSummary,DiscoveryCleanupPlan,DiscoveryCleanupBackup,DiscoveryCleanupResult,JobDataResetPlan,JobDataResetResult} from './discovery-cleanup.models';
import { ApplicationCreate, ApplicationDetail, ApplicationItem, ApplicationPackage, ApplicationPackageReadiness, ApplicationSubmissionReadiness, CandidateJobPreferences, CandidateJobPreferencesWrite, CanonicalCv, DocumentItem, EmailApplicationWorkflowResult, JobCreate, JobDetail, JobFitAnalysis, JobSummary, JobWrite, RawJobPostingSummary, ScreenshotSubmissionResult, SubmissionAttempt } from './job-hunting.models';
@Injectable({ providedIn: 'root' }) export class JobHuntingService {
 private api=inject(ApiClientService);
 startDiscoveryExecution(body:{clientRequestId:string}){return this.api.post<ApiResponse<DiscoveryExecution>>('admin/job-hunting/discovery/executions',body).pipe(map(requireData))}
 discoveryExecution(id:string){return this.api.get<ApiResponse<DiscoveryExecution>>(`admin/job-hunting/discovery/executions/${id}`).pipe(map(requireData))}
 discoveryExecutions(){return this.api.get<ApiResponse<DiscoveryExecutionHistory>>('admin/job-hunting/discovery/executions',{params:{page:1,pageSize:20}}).pipe(map(requireData))}
 jobDataResetPreview(){return this.api.get<ApiResponse<JobDataResetPlan>>('admin/job-hunting/discovery/cleanup/reset-all/preview').pipe(map(requireData))}
 resetAllJobData(body:{confirmation:string;expectedFingerprint:string}){return this.api.post<ApiResponse<JobDataResetResult>>('admin/job-hunting/discovery/cleanup/reset-all/execute',body).pipe(map(requireData))}
 discoveryCleanupRuns(){return this.api.get<ApiResponse<DiscoveryRunSummary[]>>('admin/job-hunting/discovery/cleanup/runs').pipe(map(requireData))}
 discoveryCleanupPlan(runIds:string[]){return this.api.post<ApiResponse<DiscoveryCleanupPlan>>('admin/job-hunting/discovery/demo-reset-plan',{runIds}).pipe(map(requireData))}
 discoveryCleanupBackup(runIds:string[]){return this.api.post<ApiResponse<DiscoveryCleanupBackup>>('admin/job-hunting/discovery/cleanup/backup',{runIds}).pipe(map(requireData))}
 discoveryCleanupExecute(body:{runIds:string[];expectedFingerprint:string;backup:DiscoveryCleanupBackup;backupSaved:boolean;confirmation:string}){return this.api.post<ApiResponse<DiscoveryCleanupResult>>('admin/job-hunting/discovery/cleanup/execute',body).pipe(map(requireData))}
 saveDiscoveryReview(id:string,body:HumanReviewWrite){return this.api.post<ApiResponse<HumanReview>>(`admin/job-hunting/discovery/${id}/reviews`,body).pipe(map(requireData))}
 discoveryHistory(id:string,page=1){return this.api.get<ApiResponse<ReviewHistory>>(`admin/job-hunting/discovery/${id}/reviews`,{params:{page,pageSize:20}}).pipe(map(requireData))}
 discoveryMetrics(runId?:string){return this.api.get<ApiResponse<DiscoveryQualityReport>>('admin/job-hunting/discovery/quality-metrics',{params:runId?{runId}:{}}).pipe(map(requireData))}
 discovery(params:Record<string,string|number|boolean>){return this.api.get<ApiResponse<DiscoveryReviewList<DiscoveryReviewRecord>>>('admin/job-hunting/discovery',{params}).pipe(map(requireData))}
 discoveryItem(id:string){return this.api.get<ApiResponse<DiscoveryReviewRecord>>(`admin/job-hunting/discovery/${id}`).pipe(map(requireData))}
 importDiscovery(bundle:unknown){return this.api.post<ApiResponse<{inserted:number;unchanged:number}>>('admin/job-hunting/discovery/import',bundle).pipe(map(requireData))}
 jobs(params:Record<string,string|number|boolean>){return this.api.get<ApiResponse<PagedResult<JobSummary>>>('admin/job-postings',{params}).pipe(map(requireData))} job(id:string){return this.api.get<ApiResponse<JobDetail>>(`admin/job-postings/${id}`).pipe(map(requireData))}
 createJob(body:JobCreate){return this.api.post<ApiResponse<JobDetail>>('admin/job-postings',body).pipe(map(requireData))} updateJob(id:string,body:JobWrite&{expectedVersion:number}){return this.api.put<ApiResponse<JobDetail>>(`admin/job-postings/${id}`,body).pipe(map(requireData))}
 submitScreenshots(submissionId:string,files:readonly File[]){const body=new FormData();body.append('submissionId',submissionId);for(const file of files)body.append('files',file,file.name);return this.api.post<ApiResponse<ScreenshotSubmissionResult>>('admin/raw-job-postings/screenshots',body).pipe(map(requireData))}
 rawJobPostings(params:Record<string,string|number>){return this.api.get<ApiResponse<PagedResult<RawJobPostingSummary>>>('admin/raw-job-postings',{params}).pipe(map(requireData))}
 analyzeRawJobPosting(id:string){return this.api.post<ApiResponse<JobDetail>>(`admin/raw-job-postings/${id}/analyze`,undefined).pipe(map(requireData))}
 preferences(){return this.api.get<ApiResponse<CandidateJobPreferences>>('admin/job-hunting/preferences').pipe(map(requireData))}
 updatePreferences(body:CandidateJobPreferencesWrite){return this.api.put<ApiResponse<CandidateJobPreferences>>('admin/job-hunting/preferences',body).pipe(map(requireData))}
 canonicalCv(){return this.api.get<ApiResponse<CanonicalCv>>('admin/job-hunting/canonical-cv').pipe(map(requireData))}
 uploadCanonicalCv(file:File,expectedVersion:number){const body=new FormData();body.append('file',file,file.name);body.append('expectedVersion',String(expectedVersion));return this.api.put<ApiResponse<CanonicalCv>>('admin/job-hunting/canonical-cv',body).pipe(map(requireData))}
 canonicalCvContent(){return this.api.getBlob('admin/job-hunting/canonical-cv/content')}
 analyzeFit(id:string){return this.api.get<ApiResponse<JobFitAnalysis>>(`admin/job-postings/${id}/fit-analysis`).pipe(map(requireData))}
 emailApplication(id:string,clientRequestId:string){return this.api.post<ApiResponse<EmailApplicationWorkflowResult>>(`admin/job-postings/${id}/email-application`,{clientRequestId}).pipe(map(requireData))}
 verification(id:string,status:string,expectedVersion:number){return this.api.put<ApiResponse<JobDetail>>(`admin/job-postings/${id}/verification`,{status,expectedVersion}).pipe(map(requireData))} selection(id:string,status:string,expectedVersion:number){return this.api.put<ApiResponse<JobDetail>>(`admin/job-postings/${id}/selection`,{status,expectedVersion}).pipe(map(requireData))} archive(id:string,expectedVersion:number){return this.api.post<ApiResponse<JobDetail>>(`admin/job-postings/${id}/archive`,{expectedVersion}).pipe(map(requireData))}
 applications(params:Record<string,string|number>){return this.api.get<ApiResponse<PagedResult<ApplicationItem>>>('admin/job-applications',{params}).pipe(map(requireData))} application(id:string){return this.api.get<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}`).pipe(map(requireData))} createApplication(body:ApplicationCreate){return this.api.post<ApiResponse<ApplicationDetail>>('admin/job-applications',body).pipe(map(requireData))} updateApplication(id:string,body:unknown){return this.api.put<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}`,body).pipe(map(requireData))} transition(id:string,body:unknown){return this.api.put<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}/status`,body).pipe(map(requireData))} attach(id:string,body:unknown){return this.api.post<ApiResponse<DocumentItem>>(`admin/job-applications/${id}/documents`,body).pipe(map(requireData))} remove(id:string,documentId:string){return this.api.delete<void>(`admin/job-applications/${id}/documents/${documentId}`)}
 packageReadiness(id:string){return this.api.get<ApiResponse<ApplicationPackageReadiness>>(`admin/job-hunting/applications/${id}/package-readiness`).pipe(map(requireData))}
 submissionReadiness(id:string){return this.api.get<ApiResponse<ApplicationSubmissionReadiness>>(`admin/job-hunting/applications/${id}/submission-readiness`).pipe(map(requireData))}
 submissionAttempts(id:string){return this.api.get<ApiResponse<SubmissionAttempt[]>>(`admin/job-hunting/applications/${id}/submission-attempts`).pipe(map(requireData))}
 approveSubmissionAttempt(id:string,expectedVersion:number){return this.api.post<ApiResponse<SubmissionAttempt>>(`admin/job-hunting/submission-attempts/${id}/approve`,{expectedVersion}).pipe(map(requireData))}
 executeSubmissionAttempt(id:string,expectedVersion:number){return this.api.post<ApiResponse<SubmissionAttempt>>(`admin/job-hunting/submission-attempts/${id}/execute`,{expectedVersion}).pipe(map(requireData))}
 applicationPackage(id:string){return this.api.get<ApiResponse<ApplicationPackage>>(`admin/job-hunting/applications/${id}/package`).pipe(map(requireData))}
 finalizeApplicationPackage(id:string,body:{expectedApplicationVersion:number;expectedJobPostingVersion:number;expectedCanonicalCvVersion:number}){return this.api.post<ApiResponse<ApplicationPackage>>(`admin/job-hunting/applications/${id}/package/finalize`,body).pipe(map(requireData))}
 applicationPackageCv(id:string,download=false){return this.api.getBlob(`admin/job-hunting/applications/${id}/package/cv`,{params:{download}})}
}

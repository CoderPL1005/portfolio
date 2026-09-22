import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { PagedResult } from '../../../core/api/paged-result.model';
import { requireData } from '../shared/admin-api';
import { ApplicationCreate, ApplicationDetail, ApplicationItem, ApplicationPackage, ApplicationPackageReadiness, CandidateJobPreferences, CandidateJobPreferencesWrite, CanonicalCv, DocumentItem, JobCreate, JobDetail, JobFitAnalysis, JobSummary, JobWrite, RawJobPostingSummary, ScreenshotSubmissionResult } from './job-hunting.models';
@Injectable({ providedIn: 'root' }) export class JobHuntingService {
 private api=inject(ApiClientService);
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
 verification(id:string,status:string,expectedVersion:number){return this.api.put<ApiResponse<JobDetail>>(`admin/job-postings/${id}/verification`,{status,expectedVersion}).pipe(map(requireData))} selection(id:string,status:string,expectedVersion:number){return this.api.put<ApiResponse<JobDetail>>(`admin/job-postings/${id}/selection`,{status,expectedVersion}).pipe(map(requireData))} archive(id:string,expectedVersion:number){return this.api.post<ApiResponse<JobDetail>>(`admin/job-postings/${id}/archive`,{expectedVersion}).pipe(map(requireData))}
 applications(params:Record<string,string|number>){return this.api.get<ApiResponse<PagedResult<ApplicationItem>>>('admin/job-applications',{params}).pipe(map(requireData))} application(id:string){return this.api.get<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}`).pipe(map(requireData))} createApplication(body:ApplicationCreate){return this.api.post<ApiResponse<ApplicationDetail>>('admin/job-applications',body).pipe(map(requireData))} updateApplication(id:string,body:unknown){return this.api.put<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}`,body).pipe(map(requireData))} transition(id:string,body:unknown){return this.api.put<ApiResponse<ApplicationDetail>>(`admin/job-applications/${id}/status`,body).pipe(map(requireData))} attach(id:string,body:unknown){return this.api.post<ApiResponse<DocumentItem>>(`admin/job-applications/${id}/documents`,body).pipe(map(requireData))} remove(id:string,documentId:string){return this.api.delete<void>(`admin/job-applications/${id}/documents/${documentId}`)}
 packageReadiness(id:string){return this.api.get<ApiResponse<ApplicationPackageReadiness>>(`admin/job-hunting/applications/${id}/package-readiness`).pipe(map(requireData))}
 applicationPackage(id:string){return this.api.get<ApiResponse<ApplicationPackage>>(`admin/job-hunting/applications/${id}/package`).pipe(map(requireData))}
 finalizeApplicationPackage(id:string,body:{expectedApplicationVersion:number;expectedJobPostingVersion:number;expectedCanonicalCvVersion:number}){return this.api.post<ApiResponse<ApplicationPackage>>(`admin/job-hunting/applications/${id}/package/finalize`,body).pipe(map(requireData))}
 applicationPackageCv(id:string,download=false){return this.api.getBlob(`admin/job-hunting/applications/${id}/package/cv`,{params:{download}})}
}

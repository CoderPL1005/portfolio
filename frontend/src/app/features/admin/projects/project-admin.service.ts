import { HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { EMPTY, map } from 'rxjs';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { PagedResult } from '../../../core/api/paged-result.model';
import { requireData } from '../shared/admin-api';
import { Project, ProjectListItem, ProjectMedia, ProjectRequest, ProjectSection, ProjectSectionRequest, ProjectTechnology, ReorderItem } from '../shared/admin.models';
@Injectable() export class ProjectAdminService {
  private api=inject(ApiClientService);
  list(page=1,pageSize=20,search?:string,status?:string){let p=new HttpParams().set('page',page).set('pageSize',pageSize);if(search)p=p.set('search',search);if(status)p=p.set('status',status);return this.api.get<ApiResponse<PagedResult<ProjectListItem>>>('admin/projects',{params:p}).pipe(map(requireData));}
  get(id:string){return this.api.get<ApiResponse<Project>>(`admin/projects/${id}`).pipe(map(requireData));} create(x:ProjectRequest){return this.api.post<ApiResponse<Project>>('admin/projects',x).pipe(map(requireData));} update(id:string,x:ProjectRequest){return this.api.put<ApiResponse<Project>>(`admin/projects/${id}`,x).pipe(map(requireData));} delete(id:string){return this.api.delete<void>(`admin/projects/${id}`);} reorder(items:ReorderItem[]){return this.api.put<ApiResponse<unknown>>('admin/projects/reorder',{items});}
  replaceTechnologies(id:string,technologyIds:string[]){return this.api.put<ApiResponse<ProjectTechnology[]>>(`admin/projects/${id}/technologies`,{items:technologyIds.map((technologyId,displayOrder)=>({technologyId,displayOrder}))}).pipe(map(requireData));}
  sections(id:string){return this.api.get<ApiResponse<ProjectSection[]>>(`admin/projects/${id}/sections`).pipe(map(requireData));} createSection(id:string,x:ProjectSectionRequest){return this.api.post<ApiResponse<ProjectSection>>(`admin/projects/${id}/sections`,x).pipe(map(requireData));} updateSection(id:string,sectionId:string,x:ProjectSectionRequest){return this.api.put<ApiResponse<ProjectSection>>(`admin/projects/${id}/sections/${sectionId}`,x).pipe(map(requireData));} deleteSection(id:string,sectionId:string){return window.confirm('Remove this project section?')?this.api.delete<void>(`admin/projects/${id}/sections/${sectionId}`):EMPTY;} reorderSections(id:string,items:ReorderItem[]){return this.api.put<ApiResponse<unknown>>(`admin/projects/${id}/sections/reorder`,{items});}
  media(id:string){return this.api.get<ApiResponse<ProjectMedia[]>>(`admin/projects/${id}/media`).pipe(map(requireData));} updateMedia(id:string,mediaId:string,x:{mediaRole:string;caption:string|null;displayOrder:number}){return this.api.put<ApiResponse<ProjectMedia>>(`admin/projects/${id}/media/${mediaId}`,x).pipe(map(requireData));} deleteMedia(id:string,mediaId:string){return window.confirm('Remove this media relation?')?this.api.delete<void>(`admin/projects/${id}/media/${mediaId}`):EMPTY;}
  attachMedia(id:string,x:{mediaAssetId:string;mediaRole:string;caption:string|null;displayOrder:number}){return this.api.post<ApiResponse<ProjectMedia>>(`admin/projects/${id}/media`,x).pipe(map(requireData));}
}

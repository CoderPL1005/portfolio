import { HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { PagedResult } from '../../../core/api/paged-result.model';
import { requireData } from '../shared/admin-api';
import { MediaAsset, MediaType, MediaUpdate } from './media.models';

@Injectable({providedIn:'root'})
export class MediaAdminService {
  private readonly api=inject(ApiClientService);
  list(page=1,pageSize=20,mediaType?:MediaType,search?:string){let params=new HttpParams().set('page',page).set('pageSize',pageSize);if(mediaType)params=params.set('mediaType',mediaType);if(search?.trim())params=params.set('search',search.trim());return this.api.get<ApiResponse<PagedResult<MediaAsset>>>('admin/media',{params}).pipe(map(requireData));}
  upload(file:File,mediaType:MediaType,altText:string|null){const body=new FormData();body.append('file',file,file.name);body.append('mediaType',mediaType);if(altText)body.append('altText',altText);return this.api.post<ApiResponse<MediaAsset>>('admin/media',body).pipe(map(requireData));}
  update(id:string,body:MediaUpdate){return this.api.put<ApiResponse<MediaAsset>>(`admin/media/${id}`,body).pipe(map(requireData));}
  delete(id:string){return this.api.delete<void>(`admin/media/${id}`);}
}

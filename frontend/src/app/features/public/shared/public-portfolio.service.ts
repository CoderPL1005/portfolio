import { HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { PortfolioAggregate, PublicProjectDetail, PublicProjectListItem } from './public.models';

@Injectable({ providedIn: 'root' })
export class PublicPortfolioService {
  private readonly api = inject(ApiClientService);

  getPortfolio(): Observable<PortfolioAggregate> {
    return this.api.get<ApiResponse<PortfolioAggregate>>('public/portfolio').pipe(
      map(requireData),
      map(portfolio => portfolio.technologies ? portfolio : { ...portfolio, technologies: [] }),
    );
  }

  getProjects(featured?: boolean): Observable<PublicProjectListItem[]> {
    const options = featured === undefined ? {} : { params: new HttpParams().set('featured', featured) };
    return this.api.get<ApiResponse<PublicProjectListItem[]>>('public/projects', options).pipe(map(requireData));
  }

  getProject(slug: string): Observable<PublicProjectDetail> {
    return this.api.get<ApiResponse<PublicProjectDetail>>(`public/projects/${encodeURIComponent(slug)}`).pipe(map(requireData));
  }

}

function requireData<T>(response: ApiResponse<T>): T {
  if (!response.success || response.data === undefined) {
    throw new Error('The API returned an invalid success response.');
  }
  return response.data;
}

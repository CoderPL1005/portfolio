import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../../../core/config/api-config';
import { MediaAdminService } from './media-admin.service';

describe('MediaAdminService',()=>{let service:MediaAdminService;let http:HttpTestingController;beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting(),{provide:API_BASE_URL,useValue:'https://api.example/api/v1'}]});service=TestBed.inject(MediaAdminService);http=TestBed.inject(HttpTestingController)});afterEach(()=>http.verify());
  it('lists with frozen filters',()=>{service.list(2,10,'IMAGE','school').subscribe(x=>expect(x.total).toBe(0));const req=http.expectOne(r=>r.url.endsWith('/admin/media')&&r.params.get('page')==='2'&&r.params.get('mediaType')==='IMAGE'&&r.params.get('search')==='school');expect(req.request.method).toBe('GET');req.flush({success:true,data:{items:[],page:2,pageSize:10,total:0,totalPages:0}})});
  it('uploads multipart without setting a credential or content type',()=>{service.upload(new File(['x'],'photo.png',{type:'image/png'}),'IMAGE','Portrait').subscribe();const req=http.expectOne('https://api.example/api/v1/admin/media');expect(req.request.method).toBe('POST');expect(req.request.body instanceof FormData).toBe(true);expect(req.request.headers.has('Content-Type')).toBe(false);expect((req.request.body as FormData).get('mediaType')).toBe('IMAGE');req.flush({success:true,data:{}})});
  it('updates only writable metadata',()=>{service.update('id',{altText:'Alt',mediaType:'IMAGE'}).subscribe();const req=http.expectOne('https://api.example/api/v1/admin/media/id');expect(req.request.method).toBe('PUT');expect(req.request.body).toEqual({altText:'Alt',mediaType:'IMAGE'});req.flush({success:true,data:{}})});
  it('deletes by id',()=>{service.delete('id').subscribe();const req=http.expectOne('https://api.example/api/v1/admin/media/id');expect(req.request.method).toBe('DELETE');req.flush(null)});
});

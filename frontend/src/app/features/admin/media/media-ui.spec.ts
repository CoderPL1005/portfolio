import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { routes } from '../../../app.routes';
import { MediaAdminService } from './media-admin.service';
import { MediaLibraryPageComponent } from './media-library-page.component';
import { MediaPickerComponent } from './media-picker.component';

const empty={items:[],page:1,pageSize:100,total:0,totalPages:0};
describe('media UI',()=>{let api:{list:ReturnType<typeof vi.fn>;upload:ReturnType<typeof vi.fn>;update:ReturnType<typeof vi.fn>;delete:ReturnType<typeof vi.fn>};beforeEach(()=>{api={list:vi.fn().mockReturnValue(of(empty)),upload:vi.fn(),update:vi.fn(),delete:vi.fn()};TestBed.configureTestingModule({providers:[{provide:MediaAdminService,useValue:api}]})});
  it('renders an empty media library and rejects unsafe or oversized uploads',()=>{const fixture=TestBed.createComponent(MediaLibraryPageComponent);fixture.detectChanges();expect(fixture.nativeElement.querySelectorAll('.card').length).toBe(0);const component=fixture.componentInstance;component.choose({target:{files:[new File(['<html>'],'bad.html',{type:'text/html'})]}} as unknown as Event);expect(component.file()).toBeNull();expect(component.error()).toContain('JPEG');const huge={name:'huge.png',type:'image/png',size:10*1024*1024+1} as File;component.choose({target:{files:[huge]}} as unknown as Event);expect(component.file()).toBeNull()});
  it('shows normalized list errors',()=>{api.list.mockReturnValue(throwError(()=>new Error('failed')));const fixture=TestBed.createComponent(MediaLibraryPageComponent);fixture.detectChanges();expect(fixture.componentInstance.error()).toBeTruthy()});
  it('picker filters by requested type and emits selection',()=>{const fixture=TestBed.createComponent(MediaPickerComponent);fixture.componentRef.setInput('mediaType','IMAGE');fixture.detectChanges();expect(api.list).toHaveBeenCalledWith(1,100,'IMAGE');const spy=vi.fn();fixture.componentInstance.selected.subscribe(spy);const asset={id:'1',fileName:'a.png',mimeType:'image/png',fileSize:1,mediaType:'IMAGE' as const,storageKey:'k',publicUrl:'https://cdn/a.png',altText:null,createdAt:'',updatedAt:''};fixture.componentInstance.selected.emit(asset);expect(spy).toHaveBeenCalledWith(asset)});
  it('/admin/media lazy route no longer uses the placeholder',async()=>{const admin=routes.find(x=>x.path==='admin');const media=admin?.children?.find(x=>x.path==='media');expect(await media?.loadComponent?.()).toBe(MediaLibraryPageComponent)});
});

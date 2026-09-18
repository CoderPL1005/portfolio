import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobHuntingService } from './job-hunting.service';
import { ScreenshotInboxPageComponent } from './screenshot-inbox-page.component';

describe('ScreenshotInboxPageComponent',()=>{
  let fixture:ComponentFixture<ScreenshotInboxPageComponent>;
  let api:{submitScreenshots:ReturnType<typeof vi.fn>};
  let createUrl:ReturnType<typeof vi.fn>;
  let revokeUrl:ReturnType<typeof vi.fn>;
  beforeEach(()=>{
    let sequence=0;createUrl=vi.fn(()=>`blob:preview-${++sequence}`);revokeUrl=vi.fn();
    Object.defineProperty(URL,'createObjectURL',{value:createUrl,configurable:true});Object.defineProperty(URL,'revokeObjectURL',{value:revokeUrl,configurable:true});
    api={submitScreenshots:vi.fn()};
    TestBed.configureTestingModule({imports:[ScreenshotInboxPageComponent],providers:[provideRouter([]),{provide:JobHuntingService,useValue:api}]});
    fixture=TestBed.createComponent(ScreenshotInboxPageComponent);fixture.detectChanges();
  });
  afterEach(()=>TestBed.resetTestingModule());

  it('selects multiple images, creates ordered previews, removes one, and revokes URLs',()=>{
    const files=[file('one.png','image/png',2),file('two.jpg','image/jpeg',3)];select(files);
    expect(fixture.componentInstance.previews().map(item=>item.file)).toEqual(files);expect(createUrl).toHaveBeenCalledTimes(2);expect(fixture.nativeElement.querySelectorAll('img')).toHaveLength(2);expect(fixture.nativeElement.textContent).toContain('2 selected');expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    fixture.componentInstance.remove(0);fixture.detectChanges();expect(revokeUrl).toHaveBeenCalledWith('blob:preview-1');expect(fixture.componentInstance.previews()[0].file).toBe(files[1]);
    fixture.destroy();expect(revokeUrl).toHaveBeenCalledWith('blob:preview-2');
  });

  it('rejects invalid type, count, and size without creating previews',()=>{
    select([file('photo.heic','image/heic',1)]);expect(fixture.componentInstance.validationError()).toContain('JPEG and PNG');expect(createUrl).not.toHaveBeenCalled();
    select(Array.from({length:11},(_,i)=>file(`${i}.png`,'image/png',1)));expect(fixture.componentInstance.validationError()).toContain('no more than 10');
    select([file('large.png','image/png',10*1024*1024+1)]);expect(fixture.componentInstance.validationError()).toContain('10 MiB');
  });

  it('keeps one submission id across a failed retry and clears dirty state after success',()=>{
    api.submitScreenshots.mockReturnValueOnce(throwError(()=>({status:500}))).mockReturnValueOnce(of({rawJobPostingId:'raw',ingestionStatus:'RECEIVED',attachmentCount:2,created:true}));
    const files=[file('one.png','image/png',1),file('two.png','image/png',1)];select(files);fixture.componentInstance.submit();
    expect(fixture.componentInstance.error()).toBeTruthy();expect(fixture.componentInstance.submitting()).toBe(false);expect(fixture.componentInstance.previews()).toHaveLength(2);
    fixture.componentInstance.submit();fixture.detectChanges();
    expect(api.submitScreenshots).toHaveBeenCalledTimes(2);expect(api.submitScreenshots.mock.calls[0][0]).toBe(api.submitScreenshots.mock.calls[1][0]);expect(api.submitScreenshots.mock.calls[0][1]).toEqual(files);
    expect(fixture.componentInstance.previews()).toHaveLength(0);expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);expect(fixture.nativeElement.textContent).toContain('awaiting processing');expect(revokeUrl).toHaveBeenCalledTimes(2);
  });

  it('disables duplicate submission while one multipart request is pending',()=>{
    const pending=new Subject<never>();api.submitScreenshots.mockReturnValue(pending);select([file('one.png','image/png',1)]);fixture.componentInstance.submit();fixture.detectChanges();
    expect(fixture.componentInstance.submitting()).toBe(true);expect((fixture.nativeElement.querySelector('.submit') as HTMLButtonElement).disabled).toBe(true);fixture.componentInstance.submit();expect(api.submitScreenshots).toHaveBeenCalledTimes(1);
  });

  function select(files:File[]):void{fixture.componentInstance.selectFiles({target:{files,value:'selected'}} as unknown as Event);fixture.detectChanges()}
  function file(name:string,type:string,size:number):File{return new File([new Uint8Array(size)],name,{type})}
});

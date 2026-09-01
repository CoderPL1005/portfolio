import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnologyAdminService } from './technology-admin.service';
import { TechnologyManagerComponent } from './technology-manager.component';
import { TechnologyPageComponent } from './technology-page.component';

describe('TechnologyPageComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function render() {
    const items = [{ id:'t1',name:'ASP.NET Core',category:'Backend',iconKey:null,websiteUrl:null,displayOrder:0,isActive:true,updatedAt:'' }];
    const api = { list: vi.fn(() => of(items)), create:vi.fn(() => of(items[0])), update:vi.fn(() => of(items[0])), delete:vi.fn(() => of(undefined)) };
    TestBed.overrideComponent(TechnologyManagerComponent, {
      set: { providers: [{ provide: TechnologyAdminService, useValue: api }] },
    });
    await TestBed.configureTestingModule({ imports: [TechnologyPageComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TechnologyPageComponent);
    fixture.detectChanges();
    return { fixture, api, items };
  }

  it('renders the existing global catalogue manager', async () => {
    const { fixture } = await render();

    expect(fixture.nativeElement.textContent).toContain('Global catalogue');
    expect(fixture.nativeElement.textContent).toContain('ASP.NET Core');
    expect(fixture.nativeElement.textContent).toContain('Add technology');
    expect(fixture.debugElement.query(By.directive(TechnologyManagerComponent))).not.toBeNull();
  });

  it('delegates unsaved-change checks to the existing manager', async () => {
    const { fixture } = await render();
    const manager = fixture.debugElement.query(By.directive(TechnologyManagerComponent)).componentInstance as TechnologyManagerComponent;

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    manager.form.markAsDirty();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('keeps create and edit modes on the existing CRUD service', async () => {
    const { fixture, api, items } = await render();
    const manager = fixture.debugElement.query(By.directive(TechnologyManagerComponent)).componentInstance as TechnologyManagerComponent;

    manager.form.patchValue({ name:'C#',category:'Backend' });
    manager.save();
    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ name:'C#',category:'Backend' }));
    manager.edit(items[0]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Edit technology');
    manager.save();
    expect(api.update).toHaveBeenCalledWith('t1',expect.objectContaining({ name:'ASP.NET Core' }));
  });

  it('keeps delete confirmation and deletion available', async () => {
    const { fixture, api } = await render();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find(button => button.textContent?.trim()==='Delete')!.click();
    fixture.detectChanges();
    const confirm = ([...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[]).find(button => button.textContent?.trim()==='Confirm delete')!;

    confirm.click();
    expect(api.delete).toHaveBeenCalledWith('t1');
  });
});

import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnologyAdminService } from '../technologies/technology-admin.service';
import { ProjectAdminService } from './project-admin.service';
import { ProjectEditPageComponent } from './project-edit-page.component';
import { ProjectEditorShellComponent } from './project-editor-shell.component';
import { ProjectMediaManagerComponent } from './project-media-manager.component';

describe('ProjectEditorShellComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function render() {
    const projectApi = {
      create: vi.fn(),
      sections: vi.fn(() => of([])),
      media: vi.fn(() => of([])),
    };
    const technologies = { list: vi.fn(() => of([])) };
    const route = { snapshot: { paramMap: convertToParamMap({}) } };

    TestBed.overrideComponent(ProjectEditPageComponent, {
      set: { providers: [{ provide: ProjectAdminService, useValue: projectApi }] },
    });
    TestBed.overrideComponent(ProjectMediaManagerComponent, {
      set: { providers: [{ provide: ProjectAdminService, useValue: projectApi }] },
    });
    await TestBed.configureTestingModule({
      imports: [ProjectEditorShellComponent],
      providers: [
        { provide: TechnologyAdminService, useValue: technologies },
        { provide: ActivatedRoute, useValue: route },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProjectEditorShellComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the project editor without the global technology catalogue', async () => {
    const fixture = await render();

    expect(fixture.debugElement.query(By.directive(ProjectEditPageComponent))).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-technology-manager')).toBeNull();
  });

  it('delegates unsaved-change checks only to the project editor', async () => {
    const fixture = await render();
    const shell = fixture.componentInstance;
    const editor = fixture.debugElement.query(By.directive(ProjectEditPageComponent)).componentInstance as ProjectEditPageComponent;

    expect(shell.hasUnsavedChanges()).toBe(false);
    editor.form.markAsDirty();
    expect(shell.hasUnsavedChanges()).toBe(true);
  });
});

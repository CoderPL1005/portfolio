import { Component, viewChild } from '@angular/core';
import { DirtyAware } from '../shared/dirty.guard';
import { TechnologyManagerComponent } from '../technologies/technology-manager.component';
import { ProjectEditPageComponent } from './project-edit-page.component';
import { ProjectMediaManagerComponent } from './project-media-manager.component';
@Component({selector:'app-project-editor-shell',imports:[ProjectEditPageComponent,TechnologyManagerComponent,ProjectMediaManagerComponent],template:`<app-project-edit-page/><div class="catalogue"><app-project-media-manager/><app-technology-manager/></div>`,styles:`.catalogue{padding:0 clamp(1.25rem,4vw,2.5rem) clamp(1.25rem,4vw,2.5rem)}`})
export class ProjectEditorShellComponent implements DirtyAware { private project=viewChild(ProjectEditPageComponent); private technologies=viewChild(TechnologyManagerComponent); hasUnsavedChanges(){return !!this.project()?.hasUnsavedChanges()||!!this.technologies()?.hasUnsavedChanges()} }

import { Component, viewChild } from '@angular/core';
import { MediaPickerComponent } from '../media/media-picker.component';
import { DirtyAware } from '../shared/dirty.guard';
import { ProfilePageComponent } from './profile-page.component';

@Component({selector:'app-profile-media-shell',imports:[ProfilePageComponent,MediaPickerComponent],template:`<app-profile-page/><div class="picker-wrap"><h2>Profile image</h2><app-media-picker mediaType="IMAGE" (selected)="setImage($event.id)" (cleared)="setImage('')"/><h2>CV document</h2><app-media-picker mediaType="CV" (selected)="setCv($event.id)" (cleared)="setCv('')"/></div>`,styles:`.picker-wrap{padding:0 clamp(1.25rem,4vw,2.5rem) clamp(1.25rem,4vw,2.5rem)}h2{margin-top:1.5rem}`})
export class ProfileMediaShellComponent implements DirtyAware { private readonly page=viewChild(ProfilePageComponent); setImage(id:string){this.page()?.form.controls.profileImageId.setValue(id);this.page()?.form.controls.profileImageId.markAsDirty()}setCv(id:string){this.page()?.form.controls.cvMediaId.setValue(id);this.page()?.form.controls.cvMediaId.markAsDirty()}hasUnsavedChanges(){return !!this.page()?.hasUnsavedChanges()} }

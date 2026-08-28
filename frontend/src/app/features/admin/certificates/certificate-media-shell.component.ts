import { Component, viewChild } from '@angular/core';
import { MediaPickerComponent } from '../media/media-picker.component';
import { DirtyAware } from '../shared/dirty.guard';
import { CertificatePageComponent } from './certificate-page.component';

@Component({selector:'app-certificate-media-shell',imports:[CertificatePageComponent,MediaPickerComponent],template:`<app-certificate-page/><div class="picker-wrap"><h2>Certificate document</h2><p>Select a document for the certificate currently being edited, then save the form above.</p><app-media-picker mediaType="DOCUMENT" (selected)="select($event.id)" (cleared)="select('')"/></div>`,styles:`.picker-wrap{padding:0 clamp(1.25rem,4vw,2.5rem) clamp(1.25rem,4vw,2.5rem)}`})
export class CertificateMediaShellComponent implements DirtyAware { private readonly page=viewChild(CertificatePageComponent);select(id:string){this.page()?.form.controls.certificateMediaId.setValue(id);this.page()?.form.controls.certificateMediaId.markAsDirty()}hasUnsavedChanges(){return !!this.page()?.hasUnsavedChanges()} }

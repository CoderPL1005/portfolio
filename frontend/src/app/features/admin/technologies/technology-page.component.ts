import { Component, viewChild } from '@angular/core';
import { DirtyAware } from '../shared/dirty.guard';
import { TechnologyManagerComponent } from './technology-manager.component';

@Component({
  selector: 'app-technology-page',
  imports: [TechnologyManagerComponent],
  template: `<div class="admin-page"><app-technology-manager /></div>`,
})
export class TechnologyPageComponent implements DirtyAware {
  private readonly manager = viewChild(TechnologyManagerComponent);

  hasUnsavedChanges(): boolean {
    return !!this.manager()?.hasUnsavedChanges();
  }
}

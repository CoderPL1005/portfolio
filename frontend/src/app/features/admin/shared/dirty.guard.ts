import { CanDeactivateFn } from '@angular/router';

export interface DirtyAware { hasUnsavedChanges(): boolean; }
export const unsavedChangesGuard: CanDeactivateFn<DirtyAware> = (component) => !component.hasUnsavedChanges() || window.confirm('Discard your unsaved changes?');

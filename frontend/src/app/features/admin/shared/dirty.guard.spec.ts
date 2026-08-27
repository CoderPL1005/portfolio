import { describe, expect, it, vi } from 'vitest';
import { unsavedChangesGuard } from './dirty.guard';
describe('unsavedChangesGuard', () => {
  it('allows a clean editor without prompting', () => { const confirm=vi.spyOn(window,'confirm'); expect(unsavedChangesGuard({hasUnsavedChanges:()=>false},null as never,null as never,null as never)).toBe(true); expect(confirm).not.toHaveBeenCalled(); confirm.mockRestore(); });
  it('asks before leaving a dirty editor', () => { const confirm=vi.spyOn(window,'confirm').mockReturnValue(false); expect(unsavedChangesGuard({hasUnsavedChanges:()=>true},null as never,null as never,null as never)).toBe(false); expect(confirm).toHaveBeenCalledOnce(); confirm.mockRestore(); });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { AdminSidebarComponent } from './admin-sidebar.component';

describe('AdminSidebarComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('links Technologies from the Portfolio navigation group', async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSidebarComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(AdminSidebarComponent);
    fixture.detectChanges();
    const link = fixture.nativeElement.querySelector('a[href="/admin/technologies"]') as HTMLAnchorElement;

    expect(link).not.toBeNull();
    expect(link.textContent?.trim()).toBe('Technologies');
  });
});

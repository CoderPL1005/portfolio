import { describe, expect, it } from 'vitest';
import { routes } from './app.routes';

describe('application routes', () => {
  it('defines the public shell and required public routes', () => {
    const publicShell = routes.find((route) => route.path === '' && route.children);

    expect(publicShell?.loadComponent).toBeTypeOf('function');
    expect(publicShell?.children?.map((route) => route.path)).toEqual([
      '', 'projects', 'projects/:slug', 'experience', 'skills', 'journey', 'contact',
    ]);
    expect(publicShell?.children?.every((route) => route.loadComponent)).toBe(true);
    expect(publicShell?.children?.every((route) => route.loadComponent !== placeholderReference)).toBe(true);
  });

  it('defines a guarded lazy admin login and admin shell', () => {
    const login = routes.find((route) => route.path === 'admin/login');
    const admin = routes.find((route) => route.path === 'admin');

    expect(login?.loadComponent).toBeTypeOf('function');
    expect(login?.canActivate?.length).toBe(1);
    expect(admin?.loadComponent).toBeTypeOf('function');
    expect(admin?.canActivate?.length).toBe(1);
    expect(admin?.canActivateChild?.length).toBe(1);
  });

  it('loads Phase 8 admin features while retaining only later-phase placeholders', () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    const implemented = admin.children!.filter((route) => !route.path?.startsWith('agent') && route.path !== 'media');
    const placeholders = admin.children!.filter((route) => route.path === 'media' || route.path?.startsWith('agent'));
    expect(implemented.every((route) => route.loadComponent !== placeholderReference)).toBe(true);
    expect(placeholders.every((route) => route.loadComponent === placeholderReference)).toBe(true);
    expect(admin.children!.find((route) => route.path === 'profile')?.canDeactivate?.length).toBe(1);
    expect(admin.children!.find((route) => route.path === 'projects/:id')?.canDeactivate?.length).toBe(1);
  });

  it('ends with a lazy wildcard not-found route', () => {
    const wildcard = routes.at(-1);

    expect(wildcard?.path).toBe('**');
    expect(wildcard?.loadComponent).toBeTypeOf('function');
  });
});

const placeholderReference = routes.find((route) => route.path === 'admin')?.children?.find((route) => route.path === 'media')?.loadComponent;

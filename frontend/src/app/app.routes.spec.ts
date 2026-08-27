import { describe, expect, it } from 'vitest';
import { routes } from './app.routes';

describe('application routes', () => {
  it('defines the public shell and required public routes', () => {
    const publicShell = routes.find((route) => route.path === '' && route.children);

    expect(publicShell?.loadComponent).toBeTypeOf('function');
    expect(publicShell?.children?.map((route) => route.path)).toEqual([
      '', 'projects', 'projects/:slug', 'experience', 'skills', 'journey', 'contact',
    ]);
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

  it('ends with a lazy wildcard not-found route', () => {
    const wildcard = routes.at(-1);

    expect(wildcard?.path).toBe('**');
    expect(wildcard?.loadComponent).toBeTypeOf('function');
  });
});

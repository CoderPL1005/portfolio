import { describe, expect, it } from 'vitest';
import { routes } from './app.routes';
import { ProjectEditorShellComponent } from './features/admin/projects/project-editor-shell.component';
import { TechnologyPageComponent } from './features/admin/technologies/technology-page.component';
import { NotificationSettingsPageComponent } from './features/admin/notifications/notification-settings-page.component';
import { ScreenshotInboxPageComponent } from './features/admin/job-hunting/screenshot-inbox-page.component';

describe('application routes', () => {
  it('defines the public shell and required public routes', () => {
    const publicShell = routes.find((route) => route.path === '' && route.children);

    expect(publicShell?.loadComponent).toBeTypeOf('function');
    expect(publicShell?.children?.map((route) => route.path)).toEqual([
      '', 'projects', 'projects/:slug', 'experience', 'skills', 'journey', 'contact',
    ]);
    expect(publicShell?.children?.every((route) => route.loadComponent)).toBe(true);
    expect(publicShell?.children?.every((route) => route.loadComponent)).toBe(true);
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

  it('loads all final admin features including AI routes', () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    expect(admin.children!.every((route) => route.loadComponent)).toBe(true);
    expect(admin.children!.filter((route)=>route.path?.startsWith('agent'))).toHaveLength(5);
    expect(admin.children!.some((route) => route.path?.startsWith('contact-messages'))).toBe(false);
    expect(admin.children!.find((route) => route.path === 'profile')?.canDeactivate?.length).toBe(1);
    expect(admin.children!.find((route) => route.path === 'projects/:id')?.canDeactivate?.length).toBe(1);
  });

  it('loads the project editor shell for the existing-project route', async () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    const projectRoute = admin.children!.find((route) => route.path === 'projects/:id')!;

    expect(await projectRoute.loadComponent!()).toBe(ProjectEditorShellComponent);
  });

  it('defines the guarded Technologies page inside the authenticated admin tree', async () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    const technologyRoute = admin.children!.find((route) => route.path === 'technologies')!;

    expect(technologyRoute).toBeDefined();
    expect(technologyRoute.canDeactivate).toHaveLength(1);
    expect(await technologyRoute.loadComponent!()).toBe(TechnologyPageComponent);
  });

  it('defines notification settings inside the authenticated admin tree', async () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    const notificationRoute = admin.children!.find((route) => route.path === 'notifications')!;

    expect(notificationRoute).toBeDefined();
    expect(await notificationRoute.loadComponent!()).toBe(NotificationSettingsPageComponent);
  });

  it('defines the guarded screenshot inbox before the dynamic job route', async () => {
    const admin = routes.find((route) => route.path === 'admin')!;
    const screenshotIndex = admin.children!.findIndex((route) => route.path === 'job-hunting/jobs/new/screenshots');
    const dynamicIndex = admin.children!.findIndex((route) => route.path === 'job-hunting/jobs/:id');
    const route = admin.children![screenshotIndex];
    expect(screenshotIndex).toBeGreaterThanOrEqual(0);expect(screenshotIndex).toBeLessThan(dynamicIndex);
    expect(route.canDeactivate).toHaveLength(1);expect(await route.loadComponent!()).toBe(ScreenshotInboxPageComponent);
  });

  it('ends with a lazy wildcard not-found route', () => {
    const wildcard = routes.at(-1);

    expect(wildcard?.path).toBe('**');
    expect(wildcard?.loadComponent).toBeTypeOf('function');
  });
});

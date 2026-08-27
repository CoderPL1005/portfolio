import { Routes } from '@angular/router';
import { adminAuthChildGuard, adminAuthGuard } from './core/guards/admin-auth.guard';
import { adminLoginGuard } from './core/guards/admin-login.guard';

const placeholder = () =>
  import('./features/placeholder/placeholder-page.component').then((m) => m.PlaceholderPageComponent);

export const routes: Routes = [
  {
    path: 'admin/login',
    canActivate: [adminLoginGuard],
    loadComponent: () =>
      import('./features/admin/auth/login-page.component').then((m) => m.LoginPageComponent),
  },
  {
    path: 'admin',
    canActivate: [adminAuthGuard],
    canActivateChild: [adminAuthChildGuard],
    loadComponent: () =>
      import('./layout/admin-layout/admin-layout.component').then((m) => m.AdminLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: placeholder, data: { title: 'Dashboard', eyebrow: 'Overview' } },
      { path: 'profile', loadComponent: placeholder, data: { title: 'Profile', eyebrow: 'Portfolio' } },
      { path: 'experience', loadComponent: placeholder, data: { title: 'Experience', eyebrow: 'Portfolio' } },
      { path: 'experience/new', loadComponent: placeholder, data: { title: 'New experience', eyebrow: 'Portfolio' } },
      { path: 'experience/:id', loadComponent: placeholder, data: { title: 'Edit experience', eyebrow: 'Portfolio' } },
      { path: 'education', loadComponent: placeholder, data: { title: 'Education', eyebrow: 'Portfolio' } },
      { path: 'trainings', loadComponent: placeholder, data: { title: 'Trainings', eyebrow: 'Portfolio' } },
      { path: 'certificates', loadComponent: placeholder, data: { title: 'Certificates', eyebrow: 'Portfolio' } },
      { path: 'projects', loadComponent: placeholder, data: { title: 'Projects', eyebrow: 'Portfolio' } },
      { path: 'projects/new', loadComponent: placeholder, data: { title: 'New project', eyebrow: 'Portfolio' } },
      { path: 'projects/:id', loadComponent: placeholder, data: { title: 'Edit project', eyebrow: 'Portfolio' } },
      { path: 'skills', loadComponent: placeholder, data: { title: 'Skills', eyebrow: 'Portfolio' } },
      { path: 'journey', loadComponent: placeholder, data: { title: 'Journey', eyebrow: 'Portfolio' } },
      { path: 'social-links', loadComponent: placeholder, data: { title: 'Social links', eyebrow: 'Website' } },
      { path: 'site-settings', loadComponent: placeholder, data: { title: 'Site settings', eyebrow: 'Website' } },
      { path: 'media', loadComponent: placeholder, data: { title: 'Media library', eyebrow: 'Website' } },
      { path: 'contact-messages', loadComponent: placeholder, data: { title: 'Contact messages', eyebrow: 'Inbox' } },
      { path: 'contact-messages/:id', loadComponent: placeholder, data: { title: 'Message detail', eyebrow: 'Inbox' } },
      { path: 'agent', loadComponent: placeholder, data: { title: 'Agent settings', eyebrow: 'AI Agent' } },
      { path: 'agent/knowledge', loadComponent: placeholder, data: { title: 'Knowledge', eyebrow: 'AI Agent' } },
      { path: 'agent/knowledge/:id', loadComponent: placeholder, data: { title: 'Knowledge detail', eyebrow: 'AI Agent' } },
      { path: 'agent/conversations', loadComponent: placeholder, data: { title: 'Conversations', eyebrow: 'AI Agent' } },
      { path: 'agent/conversations/:id', loadComponent: placeholder, data: { title: 'Conversation detail', eyebrow: 'AI Agent' } },
    ],
  },
  {
    path: '',
    loadComponent: () =>
      import('./layout/public-layout/public-layout.component').then((m) => m.PublicLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', loadComponent: placeholder, data: { title: 'Home', eyebrow: 'Public portfolio' } },
      { path: 'projects', loadComponent: placeholder, data: { title: 'Projects', eyebrow: 'Public portfolio' } },
      { path: 'projects/:slug', loadComponent: placeholder, data: { title: 'Project detail', eyebrow: 'Public portfolio' } },
      { path: 'experience', loadComponent: placeholder, data: { title: 'Experience', eyebrow: 'Public portfolio' } },
      { path: 'skills', loadComponent: placeholder, data: { title: 'Skills', eyebrow: 'Public portfolio' } },
      { path: 'journey', loadComponent: placeholder, data: { title: 'Journey', eyebrow: 'Public portfolio' } },
      { path: 'contact', loadComponent: placeholder, data: { title: 'Contact', eyebrow: 'Public portfolio' } },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found-page.component').then((m) => m.NotFoundPageComponent),
  },
];

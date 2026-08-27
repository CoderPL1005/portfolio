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
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/public/home/home-page.component').then(m => m.HomePageComponent) },
      { path: 'projects', loadComponent: () => import('./features/public/projects/projects-page.component').then(m => m.ProjectsPageComponent) },
      { path: 'projects/:slug', loadComponent: () => import('./features/public/project-detail/project-detail-page.component').then(m => m.ProjectDetailPageComponent) },
      { path: 'experience', loadComponent: () => import('./features/public/experience/experience-page.component').then(m => m.ExperiencePageComponent) },
      { path: 'skills', loadComponent: () => import('./features/public/skills/skills-page.component').then(m => m.SkillsPageComponent) },
      { path: 'journey', loadComponent: () => import('./features/public/journey/journey-page.component').then(m => m.JourneyPageComponent) },
      { path: 'contact', loadComponent: () => import('./features/public/contact/contact-page.component').then(m => m.ContactPageComponent) },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found-page.component').then((m) => m.NotFoundPageComponent),
  },
];

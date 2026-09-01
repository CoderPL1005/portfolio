import { Routes } from '@angular/router';
import { adminAuthChildGuard, adminAuthGuard } from './core/guards/admin-auth.guard';
import { adminLoginGuard } from './core/guards/admin-login.guard';
import { unsavedChangesGuard } from './features/admin/shared/dirty.guard';

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
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/admin/dashboard/dashboard-page.component').then(m => m.DashboardPageComponent) },
      { path: 'profile', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/profile/profile-media-shell.component').then(m => m.ProfileMediaShellComponent) },
      { path: 'experience', loadComponent: () => import('./features/admin/experiences/experience-list-page.component').then(m => m.ExperienceListPageComponent) },
      { path: 'experience/new', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/experiences/experience-edit-page.component').then(m => m.ExperienceEditPageComponent) },
      { path: 'experience/:id', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/experiences/experience-edit-page.component').then(m => m.ExperienceEditPageComponent) },
      { path: 'education', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/education/education-page.component').then(m => m.EducationPageComponent) },
      { path: 'trainings', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/trainings/training-page.component').then(m => m.TrainingPageComponent) },
      { path: 'certificates', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/certificates/certificate-media-shell.component').then(m => m.CertificateMediaShellComponent) },
      { path: 'projects', loadComponent: () => import('./features/admin/projects/project-list-page.component').then(m => m.ProjectListPageComponent) },
      { path: 'projects/new', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/projects/project-editor-shell.component').then(m => m.ProjectEditorShellComponent) },
      { path: 'projects/:id', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/projects/project-editor-shell.component').then(m => m.ProjectEditorShellComponent) },
      { path: 'skills', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/skills/skills-page.component').then(m => m.SkillsAdminPageComponent) },
      { path: 'technologies', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/technologies/technology-page.component').then(m => m.TechnologyPageComponent) },
      { path: 'journey', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/journey/journey-page.component').then(m => m.JourneyAdminPageComponent) },
      { path: 'social-links', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/social-links/social-links-page.component').then(m => m.SocialLinksPageComponent) },
      { path: 'site-settings', canDeactivate: [unsavedChangesGuard], loadComponent: () => import('./features/admin/site-settings/site-settings-page.component').then(m => m.SiteSettingsPageComponent) },
      { path: 'media', loadComponent: () => import('./features/admin/media/media-library-page.component').then(m => m.MediaLibraryPageComponent) },
      { path: 'agent', canDeactivate:[unsavedChangesGuard], loadComponent:()=>import('./features/admin/agent/agent-settings-page.component').then(m=>m.AgentSettingsPageComponent) },
      { path: 'agent/knowledge', loadComponent:()=>import('./features/admin/agent/knowledge-page.component').then(m=>m.KnowledgePageComponent) },
      { path: 'agent/knowledge/:id', loadComponent:()=>import('./features/admin/agent/knowledge-detail-page.component').then(m=>m.KnowledgeDetailPageComponent) },
      { path: 'agent/conversations', loadComponent:()=>import('./features/admin/agent/conversations-page.component').then(m=>m.ConversationsPageComponent) },
      { path: 'agent/conversations/:id', loadComponent:()=>import('./features/admin/agent/conversation-detail-page.component').then(m=>m.ConversationDetailPageComponent) },
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

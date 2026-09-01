import { Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface AdminNavigationGroup {
  label: string;
  links: { label: string; path: string }[];
}

@Component({
  selector: 'app-admin-sidebar',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar" [class.open]="open()" aria-label="Admin navigation">
      <div class="sidebar-header">
        <a routerLink="/admin" (click)="navigate.emit()"><span aria-hidden="true">&gt;_</span> PORTFOLIO CMS</a>
        <button type="button" (click)="navigate.emit()" aria-label="Close admin navigation">×</button>
      </div>
      <nav>
        @for (group of groups; track group.label) {
          <section>
            <p>{{ group.label }}</p>
            @for (link of group.links; track link.path) {
              <a [routerLink]="link.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: link.path === '/admin' }" (click)="navigate.emit()">{{ link.label }}</a>
            }
          </section>
        }
      </nav>
    </aside>
  `,
  styleUrl: './admin-sidebar.component.css',
})
export class AdminSidebarComponent {
  readonly open = input(false);
  readonly navigate = output<void>();
  readonly groups: AdminNavigationGroup[] = [
    { label: 'Overview', links: [{ label: 'Dashboard', path: '/admin' }] },
    { label: 'Portfolio', links: [
      { label: 'Profile', path: '/admin/profile' }, { label: 'Experience', path: '/admin/experience' },
      { label: 'Education', path: '/admin/education' }, { label: 'Trainings', path: '/admin/trainings' },
      { label: 'Certificates', path: '/admin/certificates' }, { label: 'Projects', path: '/admin/projects' },
      { label: 'Skills', path: '/admin/skills' }, { label: 'Technologies', path: '/admin/technologies' },
      { label: 'Journey', path: '/admin/journey' },
    ] },
    { label: 'Website', links: [
      { label: 'Social Links', path: '/admin/social-links' }, { label: 'Site Settings', path: '/admin/site-settings' },
      { label: 'Media Library', path: '/admin/media' },
    ] },
    { label: 'AI Agent', links: [
      { label: 'Knowledge', path: '/admin/agent/knowledge' }, { label: 'Agent Settings', path: '/admin/agent' },
      { label: 'Conversations', path: '/admin/agent/conversations' },
    ] },
  ];
}

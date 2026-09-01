import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ChatWidgetComponent } from '../../features/agent/chat-widget.component';
import { PortfolioStore } from '../../features/public/shared/portfolio.store';
import { isExternalUrl, safeHttpUrl, safeSocialUrl } from '../../features/public/shared/public-utils';
import { SocialIconComponent } from '../../shared/components/social-icon/social-icon.component';

@Component({
  selector: 'app-public-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ChatWidgetComponent, SocialIconComponent],
  templateUrl: './public-layout.component.html',
  styleUrl: './public-layout.component.css',
})
export class PublicLayoutComponent {
  readonly store = inject(PortfolioStore);
  readonly menuOpen = signal(false);
  readonly profile = computed(() => this.store.data()?.profile ?? null);
  readonly socialLinks = computed(() => this.store.data()?.socialLinks ?? []);
  readonly initials = computed(() => (this.profile()?.fullName ?? 'Portfolio').split(/\s+/).filter(Boolean).map(x => x[0]).slice(-3).join('').toUpperCase());
  readonly http = safeHttpUrl;
  readonly socialUrl = safeSocialUrl;
  readonly external = isExternalUrl;
  readonly links = [
    { label: 'Home', path: '/', fragment: undefined },
    { label: 'Experience', path: '/experience', fragment: undefined },
    { label: 'Projects', path: '/projects', fragment: undefined },
    { label: 'Skills', path: '/skills', fragment: undefined },
    { label: 'Journey', path: '/journey', fragment: undefined },
    { label: 'Contact', path: '/contact', fragment: undefined },
  ];

  constructor() { this.store.load(true); }

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
  }
}

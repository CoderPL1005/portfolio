import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-admin-topbar',
  imports: [RouterLink],
  template: `
    <header>
      <button class="menu" type="button" (click)="menu.emit()" aria-label="Open admin navigation">Menu</button>
      <span class="context">Admin workspace</span>
      <div class="actions">
        <a routerLink="/">View site</a>
        <span class="admin-name">{{ adminName() || 'Administrator' }}</span>
        <button type="button" (click)="logout.emit()" [disabled]="loggingOut()">{{ loggingOut() ? 'Signing out…' : 'Logout' }}</button>
      </div>
    </header>
  `,
  styles: `
    header { height: 4rem; display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 0 1.5rem; border-bottom: 1px solid var(--color-border); background: color-mix(in srgb, var(--color-surface) 88%, transparent); backdrop-filter: blur(18px); }
    .context { color: var(--color-text-dim); font-size: .75rem; text-transform: uppercase; letter-spacing: .12em; }
    .actions { display: flex; align-items: center; gap: 1rem; }
    a { padding: .45rem .7rem; border-radius: var(--radius-sm); color: var(--color-on-primary); background: var(--color-primary); text-decoration: none; font-size: .8rem; font-weight: 700; }
    button { border: 0; color: var(--color-text-muted); background: transparent; cursor: pointer; }
    button:hover { color: var(--color-error); }
    .menu { display: none; color: var(--color-text); }
    .admin-name { color: var(--color-text-muted); font-size: .82rem; }
    @media (max-width: 900px) { .menu { display: block; } .context, .admin-name { display: none; } header { padding: 0 1rem; } }
  `,
})
export class AdminTopbarComponent {
  readonly adminName = input<string | null>();
  readonly loggingOut = input(false);
  readonly menu = output<void>();
  readonly logout = output<void>();
}

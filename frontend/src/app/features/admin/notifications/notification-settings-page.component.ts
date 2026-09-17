import { Component, inject } from '@angular/core';
import { AdminPageHeaderComponent } from '../shared/admin-page-header.component';
import { PushNotificationService } from './push-notification.service';

@Component({
  selector: 'app-notification-settings-page',
  imports: [AdminPageHeaderComponent],
  providers: [PushNotificationService],
  template: `
    <div class="admin-page">
      <app-admin-page-header
        title="Notifications"
        eyebrow="Website"
        description="Manage Web Push for this installed owner PWA."
      />

      <section class="admin-panel notification-panel" aria-labelledby="notification-status">
        <h2 id="notification-status">Web Push</h2>

        @switch (push.status()) {
          @case ('unsupported') {
            <p>Notifications are not supported on this device or browser.</p>
          }
          @case ('unconfigured') {
            <p>Notifications are not configured for this deployment.</p>
          }
          @case ('denied') {
            <p>Notifications are blocked in system or browser settings.</p>
          }
          @case ('not-enabled') {
            <p>Notifications are not enabled.</p>
            <button class="admin-button primary" type="button" [disabled]="push.busy()" (click)="enable()">
              {{ push.busy() ? 'Enabling…' : 'Enable notifications' }}
            </button>
            <p class="guidance">On iPhone, install the site to your Home Screen and open the installed app before enabling notifications.</p>
          }
          @case ('enabled') {
            <p class="enabled">Notifications enabled</p>
            <div class="actions">
              <button class="admin-button primary" type="button" [disabled]="push.busy()" (click)="sendTest()">
                Send test notification
              </button>
              <button class="admin-button" type="button" [disabled]="push.busy()" (click)="disable()">
                Disable notifications
              </button>
            </div>
          }
        }

        @if (push.error(); as error) {
          <p class="admin-error" role="alert">{{ error }}</p>
        }
        @if (push.summary(); as summary) {
          <p class="admin-success" role="status">
            Attempted {{ summary.attempted }}; delivered {{ summary.succeeded }}; deactivated
            {{ summary.deactivated }}; failed {{ summary.failed }}.
          </p>
        }
      </section>
    </div>
  `,
  styles: `
    .notification-panel { max-width: 42rem; }
    .notification-panel h2 { margin-top: 0; }
    .notification-panel p { color: var(--color-text-muted); }
    .notification-panel .enabled { color: var(--color-text); font-weight: 700; }
    .actions { display: flex; flex-wrap: wrap; gap: .75rem; }
    .guidance { max-width: 36rem; font-size: .875rem; }
  `,
})
export class NotificationSettingsPageComponent {
  readonly push = inject(PushNotificationService);

  enable(): void {
    void this.push.enable();
  }

  disable(): void {
    void this.push.disable();
  }

  sendTest(): void {
    void this.push.sendTest();
  }
}

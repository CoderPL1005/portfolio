import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiClientService } from '../../../core/api/api-client.service';
import { ApiResponse } from '../../../core/api/api-response.model';
import { environment } from '../../../../environments/environment';

export type PushNotificationStatus =
  | 'unsupported'
  | 'unconfigured'
  | 'not-enabled'
  | 'enabled'
  | 'denied';

export interface PushTestSummary {
  attempted: number;
  succeeded: number;
  deactivated: number;
  failed: number;
}

@Injectable()
export class PushNotificationService {
  private readonly swPush = inject(SwPush);
  private readonly api = inject(ApiClientService);
  private readonly destroyRef = inject(DestroyRef);
  private currentSubscription: PushSubscription | null = null;

  readonly status = signal<PushNotificationStatus>(this.initialStatus());
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<PushTestSummary | null>(null);

  constructor() {
    if (!this.swPush.isEnabled) {
      return;
    }

    this.swPush.subscription.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (subscription) => {
        this.currentSubscription = subscription;
        this.refreshStatus();
      },
      error: () => {
        this.currentSubscription = null;
        this.status.set('unsupported');
      },
    });
  }

  async enable(): Promise<void> {
    if (this.busy() || this.status() === 'unsupported' || this.status() === 'unconfigured' || this.status() === 'denied') {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.summary.set(null);
    let subscription: PushSubscription | null = null;

    try {
      // This call intentionally remains the first asynchronous operation in the click path.
      subscription = await this.swPush.requestSubscription({
        serverPublicKey: environment.webPushPublicKey,
      });
      const json = subscription.toJSON();
      const p256dh = json.keys?.['p256dh'];
      const auth = json.keys?.['auth'];
      if (!p256dh || !auth) {
        throw new Error('The browser returned an incomplete push subscription.');
      }

      await firstValueFrom(this.api.post<ApiResponse<unknown>>('admin/push/subscriptions', {
        endpoint: subscription.endpoint,
        p256dh,
        auth,
      }));
      this.currentSubscription = subscription;
      this.status.set('enabled');
    } catch {
      if (subscription) {
        try {
          await this.swPush.unsubscribe();
        } catch {
          // Backend registration still failed; do not expose browser/provider details.
        }
      }
      this.currentSubscription = null;
      this.refreshStatus();
      this.error.set(
        this.status() === 'denied'
          ? 'Notifications are blocked in system or browser settings.'
          : 'Notifications could not be enabled. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  async disable(): Promise<void> {
    if (this.busy() || !this.currentSubscription) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.summary.set(null);

    try {
      await firstValueFrom(this.api.delete<ApiResponse<unknown>>('admin/push/subscriptions', {
        body: { endpoint: this.currentSubscription.endpoint },
      }));
      await this.swPush.unsubscribe();
      this.currentSubscription = null;
      this.refreshStatus();
    } catch {
      this.error.set('Notifications could not be disabled. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  async sendTest(): Promise<void> {
    if (this.busy() || this.status() !== 'enabled') {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.summary.set(null);

    try {
      const response = await firstValueFrom(
        this.api.post<ApiResponse<PushTestSummary>>('admin/push/test', {}),
      );
      if (!response.data) {
        throw new Error('The server returned no delivery summary.');
      }
      this.summary.set(response.data);
    } catch {
      this.error.set('The test notification could not be sent. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private initialStatus(): PushNotificationStatus {
    if (!this.swPush.isEnabled || typeof Notification === 'undefined') {
      return 'unsupported';
    }
    if (!environment.webPushPublicKey) {
      return 'unconfigured';
    }
    return Notification.permission === 'denied' ? 'denied' : 'not-enabled';
  }

  private refreshStatus(): void {
    if (!this.swPush.isEnabled || typeof Notification === 'undefined') {
      this.status.set('unsupported');
    } else if (!environment.webPushPublicKey) {
      this.status.set('unconfigured');
    } else if (Notification.permission === 'denied') {
      this.status.set('denied');
    } else {
      this.status.set(this.currentSubscription ? 'enabled' : 'not-enabled');
    }
  }
}

import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiClientService } from '../../../core/api/api-client.service';
import { environment } from '../../../../environments/environment';
import { PushNotificationService } from './push-notification.service';

describe('PushNotificationService', () => {
  const originalNotification = Object.getOwnPropertyDescriptor(globalThis, 'Notification');
  let subscriptions: BehaviorSubject<PushSubscription | null>;
  let swPush: {
    isEnabled: boolean;
    subscription: BehaviorSubject<PushSubscription | null>;
    requestSubscription: ReturnType<typeof vi.fn>;
    unsubscribe: ReturnType<typeof vi.fn>;
  };
  let api: {
    post: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    environment.webPushPublicKey = 'test-vapid-public-key';
    setPermission('default');
    subscriptions = new BehaviorSubject<PushSubscription | null>(null);
    swPush = {
      isEnabled: true,
      subscription: subscriptions,
      requestSubscription: vi.fn(),
      unsubscribe: vi.fn().mockResolvedValue(undefined),
    };
    api = {
      post: vi.fn(() => of({ success: true })),
      delete: vi.fn(() => of({ success: true })),
    };
  });

  afterEach(() => {
    environment.webPushPublicKey = '';
    if (originalNotification) {
      Object.defineProperty(globalThis, 'Notification', originalNotification);
    } else {
      Reflect.deleteProperty(globalThis, 'Notification');
    }
  });

  it('reports unsupported without requesting permission', () => {
    swPush.isEnabled = false;
    const service = createService();

    expect(service.status()).toBe('unsupported');
    expect(swPush.requestSubscription).not.toHaveBeenCalled();
  });

  it('reports denied and never prompts again from enable', async () => {
    setPermission('denied');
    const service = createService();

    await service.enable();

    expect(service.status()).toBe('denied');
    expect(swPush.requestSubscription).not.toHaveBeenCalled();
  });

  it('requests subscription only on explicit enable and serializes keys for backend registration', async () => {
    const subscription = fakeSubscription(
      'https://push.example/private-endpoint',
      'browser-p256dh',
      'browser-auth',
    );
    swPush.requestSubscription.mockResolvedValue(subscription);
    const service = createService();
    expect(swPush.requestSubscription).not.toHaveBeenCalled();

    await service.enable();

    expect(swPush.requestSubscription).toHaveBeenCalledOnce();
    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: 'test-vapid-public-key' });
    expect(api.post).toHaveBeenCalledWith('admin/push/subscriptions', {
      endpoint: 'https://push.example/private-endpoint',
      p256dh: 'browser-p256dh',
      auth: 'browser-auth',
    });
    expect(service.status()).toBe('enabled');
    expect(service.busy()).toBe(false);
  });

  it('disables the backend endpoint before removing the browser subscription', async () => {
    const subscription = fakeSubscription('https://push.example/disable', 'p256dh', 'auth');
    subscriptions.next(subscription);
    const service = createService();

    await service.disable();

    expect(api.delete).toHaveBeenCalledWith('admin/push/subscriptions', {
      body: { endpoint: 'https://push.example/disable' },
    });
    expect(swPush.unsubscribe).toHaveBeenCalledOnce();
    expect(service.status()).toBe('not-enabled');
    expect(service.busy()).toBe(false);
  });

  it('sends a server-controlled test notification and exposes only the summary', async () => {
    subscriptions.next(fakeSubscription('https://push.example/test', 'p256dh', 'auth'));
    api.post.mockReturnValue(of({
      success: true,
      data: { attempted: 1, succeeded: 1, deactivated: 0, failed: 0 },
    }));
    const service = createService();

    await service.sendTest();

    expect(api.post).toHaveBeenCalledWith('admin/push/test', {});
    expect(service.summary()).toEqual({ attempted: 1, succeeded: 1, deactivated: 0, failed: 0 });
    expect(service.busy()).toBe(false);
  });

  it('rolls back failed registration and never exposes subscription secrets in errors', async () => {
    const subscription = fakeSubscription('https://push.example/secret-endpoint', 'secret-p256dh', 'secret-auth');
    swPush.requestSubscription.mockResolvedValue(subscription);
    api.post.mockReturnValue(throwError(() => new Error(
      'https://push.example/secret-endpoint secret-p256dh secret-auth',
    )));
    const service = createService();

    await service.enable();

    expect(swPush.unsubscribe).toHaveBeenCalledOnce();
    expect(service.status()).toBe('not-enabled');
    expect(service.busy()).toBe(false);
    expect(service.error()).toBe('Notifications could not be enabled. Please try again.');
    expect(service.error()).not.toContain('secret');
  });

  function createService(): PushNotificationService {
    TestBed.configureTestingModule({
      providers: [
        PushNotificationService,
        { provide: SwPush, useValue: swPush },
        { provide: ApiClientService, useValue: api },
      ],
    });
    return TestBed.inject(PushNotificationService);
  }

  function setPermission(permission: NotificationPermission): void {
    Object.defineProperty(globalThis, 'Notification', {
      configurable: true,
      value: { permission },
    });
  }

  function fakeSubscription(endpoint: string, p256dh: string, auth: string): PushSubscription {
    return {
      endpoint,
      toJSON: () => ({ endpoint, keys: { p256dh, auth } }),
    } as unknown as PushSubscription;
  }
});

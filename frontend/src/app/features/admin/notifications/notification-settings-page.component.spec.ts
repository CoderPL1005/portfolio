import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationSettingsPageComponent } from './notification-settings-page.component';
import { PushNotificationService, PushNotificationStatus } from './push-notification.service';

describe('NotificationSettingsPageComponent', () => {
  const push = {
    status: signal<PushNotificationStatus>('unsupported'),
    busy: signal(false),
    error: signal<string | null>(null),
    summary: signal(null),
    enable: vi.fn().mockResolvedValue(undefined),
    disable: vi.fn().mockResolvedValue(undefined),
    sendTest: vi.fn().mockResolvedValue(undefined),
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    push.status.set('unsupported');
    push.busy.set(false);
    push.error.set(null);
    push.summary.set(null);
    vi.clearAllMocks();
  });

  it('renders unsupported and denied guidance', async () => {
    const fixture = await render();
    expect(fixture.nativeElement.textContent).toContain('Notifications are not supported');

    push.status.set('denied');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('blocked in system or browser settings');
  });

  it('keeps permission request behind the explicit Enable button', async () => {
    push.status.set('not-enabled');
    const fixture = await render();
    const button = findButton(fixture.nativeElement, 'Enable notifications');

    button.click();

    expect(push.enable).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('install the site to your Home Screen');
  });

  it('renders enabled actions for test and disable', async () => {
    push.status.set('enabled');
    const fixture = await render();

    findButton(fixture.nativeElement, 'Send test notification').click();
    findButton(fixture.nativeElement, 'Disable notifications').click();

    expect(push.sendTest).toHaveBeenCalledOnce();
    expect(push.disable).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('Notifications enabled');
  });

  async function render() {
    TestBed.overrideComponent(NotificationSettingsPageComponent, {
      set: { providers: [{ provide: PushNotificationService, useValue: push }] },
    });
    await TestBed.configureTestingModule({ imports: [NotificationSettingsPageComponent] }).compileComponents();
    const fixture = TestBed.createComponent(NotificationSettingsPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  function findButton(element: HTMLElement, label: string): HTMLButtonElement {
    return ([...element.querySelectorAll('button')] as HTMLButtonElement[])
      .find((button) => button.textContent?.trim() === label)!;
  }
});

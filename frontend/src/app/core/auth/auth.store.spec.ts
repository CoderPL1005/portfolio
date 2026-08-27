import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthStore } from './auth.store';

describe('AuthStore', () => {
  let store: AuthStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    store = TestBed.inject(AuthStore);
  });

  it('starts with an unknown authentication state and no token', () => {
    expect(store.status()).toBe('unknown');
    expect(store.accessToken()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
  });

  it('stores the authenticated admin and access token in memory', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: 'Admin' }, 'access-token');

    expect(store.status()).toBe('authenticated');
    expect(store.admin()?.email).toBe('admin@example.com');
    expect(store.accessToken()).toBe('access-token');
  });

  it('clears all authentication state', () => {
    store.authenticate({ id: '1', email: 'admin@example.com', fullName: null }, 'access-token');
    store.clear();

    expect(store.status()).toBe('anonymous');
    expect(store.admin()).toBeNull();
    expect(store.accessToken()).toBeNull();
  });
});

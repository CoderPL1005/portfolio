import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  const auth = { login: vi.fn() };
  const router = { navigateByUrl: vi.fn() };
  let component: LoginPageComponent;

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
    ] });
    component = TestBed.runInInjectionContext(() => new LoginPageComponent());
  });

  it('blocks an invalid form without calling AuthService', () => {
    component.submit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(component.form.controls.email.touched).toBe(true);
  });

  it('submits valid credentials and navigates to the admin shell', () => {
    auth.login.mockReturnValue(of({ id: '1', email: 'admin@example.com', fullName: null }));
    component.form.setValue({ email: 'admin@example.com', password: 'password' });

    component.submit();

    expect(auth.login).toHaveBeenCalledWith({ email: 'admin@example.com', password: 'password' });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin');
    expect(component.form.controls.password.value).toBe('');
  });
});

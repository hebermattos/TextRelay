import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { LoginComponent } from './login.component';
import { AuthService } from '../core/auth.service';

describe('Login screen', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => { TestBed.inject(AuthService).ngOnDestroy(); http.verify(); vi.restoreAllMocks(); });

  it('toggles password visibility and resets credentials when switching account type', async () => {
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges(); await fixture.whenStable();
    const component = fixture.componentInstance;
    expect(fixture.nativeElement.querySelector('#credential').type).toBe('password');
    fixture.nativeElement.querySelector('.signin-reveal').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#credential').type).toBe('text');
    component.credential = 'do-not-keep';
    fixture.nativeElement.querySelectorAll('.signin-modes button')[1].click();
    fixture.detectChanges(); await fixture.whenStable();
    expect(component.mode()).toBe('platform'); expect(component.credential).toBe('');
    expect(fixture.nativeElement.querySelector('#credential').type).toBe('password');
    expect(fixture.nativeElement.querySelector('#login-identity')).not.toBeNull();
  });

  it('requires an administrator username and submits a password instead of a shared key', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;
    component.changeMode('platform'); component.credential = 'administrator-password';
    component.submit(); http.expectNone('/api/v1/admin/auth/token');
    component.username = ' admin '; component.submit();
    const request = http.expectOne('/api/v1/admin/auth/token');
    expect(request.request.body).toEqual({ username: 'admin', password: 'administrator-password' });
    request.flush({ access_token: `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 900 }))}.s` });
    expect(component.credential).toBe('');
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/admin/tenants');
  });

  it('prevents duplicate requests and clears credentials after a login error', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    const component = fixture.componentInstance;
    component.clientId = ' client '; component.credential = 'secret';
    component.submit(); component.submit(); component.changeMode('platform');
    const request = http.expectOne('/api/v1/auth/token');
    expect(request.request.body).toEqual({ clientId: 'client', clientSecret: 'secret' });
    expect(component.busy()).toBe(true); expect(component.mode()).toBe('client');
    request.flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(component.busy()).toBe(false); expect(component.credential).toBe('');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Invalid credentials');
  });

  it('redirects an existing session to the workspace without logging in again', () => {
    const token = `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 900 }))}.s`;
    sessionStorage.setItem('sms-ui-session', JSON.stringify({ token, role: 'tenant', identity: 'client' }));
    TestBed.createComponent(LoginComponent);
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledWith('/app');
    http.expectNone('/api/v1/auth/token');
  });
});

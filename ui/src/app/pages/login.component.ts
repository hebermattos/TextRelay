import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { errorMessage } from '../core/api';
import { IconComponent } from '../shared/icon.component';

type LoginMode = 'client' | 'platform';

@Component({
  selector: 'sms-login',
  imports: [FormsModule, IconComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent {
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly mode = signal<LoginMode>('client');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly showCredential = signal(false);
  readonly capsLock = signal(false);

  clientId = '';
  username = '';
  credential = '';

  constructor() {
    if (this.auth.bearer()) void this.router.navigateByUrl(this.auth.role() === 'admin' ? '/admin/tenants' : '/app');
  }

  changeMode(mode: LoginMode) {
    if (this.busy()) return;
    this.mode.set(mode);
    this.credential = '';
    this.error.set('');
    this.showCredential.set(false);
    this.capsLock.set(false);
  }

  checkCapsLock(event: KeyboardEvent) {
    this.capsLock.set(event.getModifierState('CapsLock'));
  }

  submit() {
    const identifier = this.mode() === 'client' ? this.clientId.trim() : this.username.trim();
    if (this.busy() || !this.credential || !identifier) return;

    this.busy.set(true);
    this.error.set('');
    const request = this.mode() === 'client'
      ? this.auth.loginTenant(identifier, this.credential)
      : this.auth.loginAdmin(identifier, this.credential);

    request.pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => {
        this.busy.set(false);
        this.credential = '';
        this.showCredential.set(false);
      })
    ).subscribe({
      next: () => void this.router.navigateByUrl(this.auth.role() === 'admin' ? '/admin/tenants' : '/app'),
      error: error => this.error.set(errorMessage(error))
    });
  }
}

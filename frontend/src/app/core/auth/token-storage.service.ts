import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  private readonly token = signal<string | null>(null);
  readonly accessToken = this.token.asReadonly();

  set(token: string): void {
    this.token.set(token);
  }

  clear(): void {
    this.token.set(null);
  }
}

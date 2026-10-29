import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from './api-errors';

interface AdminUser { id: string; email: string; emailConfirmed: boolean; isApproved: boolean; }

// New accounts can't sign in until an admin approves them. Admins see this on the Settings page;
// for everyone else the list request is refused and nothing shows.
@Component({
  selector: 'app-account-approvals',
  template: `
    @if (users(); as list) {
      <section class="card">
        <h2>Accounts waiting for approval</h2>
        @if (waiting().length === 0) {
          <p class="small-text muted">No accounts are waiting.</p>
        } @else {
          <ul class="approval-list">
            @for (u of waiting(); track u.id) {
              <li class="row">
                <span class="grow">{{ u.email }}
                  @if (!u.emailConfirmed) { <span class="small-text muted">(email not confirmed yet)</span> }
                </span>
                <button type="button" class="primary" (click)="approve(u)" [disabled]="busy()">Approve</button>
                <button type="button" (click)="decline(u)" [disabled]="busy()">Decline</button>
              </li>
            }
          </ul>
        }
        <p class="small-text muted">{{ approvedCount() }} approved account{{ approvedCount() === 1 ? '' : 's' }}.
          Declining deletes the waiting account.</p>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      </section>
    }
  `
})
export class AccountApprovals {
  private http = inject(HttpClient);

  users = signal<AdminUser[] | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);

  waiting = () => (this.users() ?? []).filter(u => !u.isApproved);
  approvedCount = () => (this.users() ?? []).filter(u => u.isApproved).length;

  constructor() {
    void this.load();
  }

  private async load() {
    try {
      this.users.set(await firstValueFrom(this.http.get<AdminUser[]>('/api/admin/users')));
    } catch {
      this.users.set(null); // not an admin: show nothing
    }
  }

  approve(u: AdminUser) {
    return this.run(() => firstValueFrom(this.http.post(`/api/admin/users/${encodeURIComponent(u.id)}/approve`, null)),
      `Couldn't approve ${u.email}. Please try again.`);
  }

  decline(u: AdminUser) {
    if (!confirm(`Decline ${u.email}? Their account will be deleted.`)) return Promise.resolve();
    return this.run(() => firstValueFrom(this.http.delete(`/api/admin/users/${encodeURIComponent(u.id)}`)),
      `Couldn't decline ${u.email}. Please try again.`);
  }

  private async run(action: () => Promise<unknown>, fallback: string) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load();
    } catch (e) {
      this.error.set(apiErrorMessage(e, fallback));
    } finally {
      this.busy.set(false);
    }
  }
}

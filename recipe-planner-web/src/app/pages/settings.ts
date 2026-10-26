import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth.service';
import { Diet, Settings, SettingsService, Theme } from '../settings.service';
import { ServingsStepper } from '../servings-stepper';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, ServingsStepper],
  template: `
    <div class="settings">
      <h1>Settings</h1>
      <p class="muted">Signed in as <strong>{{ email() }}</strong>. Changes save as you make them and follow you to every device you sign in on.</p>

      <section class="card">
        <h2>Theme</h2>
        <div class="theme-options" role="radiogroup" aria-label="Theme">
          @for (t of themes; track t.value) {
            <label class="theme-option">
              <input type="radio" name="theme" [value]="t.value" [checked]="current().theme === t.value" (change)="update({ theme: t.value })" />
              <span class="theme-swatch {{ t.value }}" aria-hidden="true"></span>
              {{ t.label }}
            </label>
          }
        </div>
      </section>

      <section class="card">
        <h2>Diet</h2>
        <p class="small-text muted">Browse, the world map and "Plan my week" start filtered to this diet. You can still turn the filter off on each page.</p>
        <label class="field">Eating plan
          <select [value]="current().diet ?? ''" (change)="update({ diet: ($any($event.target).value || null) })">
            <option value="">No restrictions</option>
            <option value="AIP">AIP (autoimmune protocol)</option>
            <option value="Vegetarian">Vegetarian</option>
          </select>
        </label>
      </section>

      <section class="card">
        <h2>Servings</h2>
        <p class="small-text muted">New recipes and planned meals start at this many servings.</p>
        <app-servings-stepper [value]="current().defaultServings" [base]="current().defaultServings"
                              (valueChange)="update({ defaultServings: $event })" />
      </section>
      @if (saveNote()) { <p [class]="saveFailed() ? 'error' : 'saved-note'" role="status">{{ saveNote() }}</p> }

      <section class="card">
        <h2>Password</h2>
        <form (ngSubmit)="changePassword()" class="form">
          <label>Current password
            <input type="password" name="oldPassword" [(ngModel)]="oldPassword" autocomplete="current-password" required />
          </label>
          <label>New password
            <input type="password" name="newPassword" [(ngModel)]="newPassword" autocomplete="new-password" minlength="12" required />
            <span class="small-text muted">At least 12 characters. A few random words works well.</span>
          </label>
          <button type="submit" [disabled]="pwBusy()">{{ pwBusy() ? 'Changing…' : 'Change password' }}</button>
          @if (pwNote()) { <p [class]="pwFailed() ? 'error' : 'saved-note'" role="status">{{ pwNote() }}</p> }
        </form>
      </section>

      <section class="card danger-zone">
        <h2>Delete account</h2>
        <p class="small-text">Permanently deletes your account, recipes, photos, meal plans, shopping list, ratings and Kroger connection.
          This can't be undone. Library recipes and other people's shared recipes aren't affected.</p>
        <form (ngSubmit)="deleteAccount()" class="form">
          <label>Your password
            <input type="password" name="deletePassword" [(ngModel)]="deletePassword" autocomplete="current-password" required />
          </label>
          <label class="check-label"><input type="checkbox" name="confirm" [(ngModel)]="confirmDelete" /> I understand this deletes everything</label>
          <button type="submit" class="danger" [disabled]="!confirmDelete || !deletePassword || deleteBusy()">
            {{ deleteBusy() ? 'Deleting…' : 'Delete my account' }}
          </button>
          @if (deleteError()) { <p class="error" role="alert">{{ deleteError() }}</p> }
        </form>
      </section>
    </div>
  `
})
export class SettingsPage {
  private settingsService = inject(SettingsService);
  private auth = inject(AuthService);
  private http = inject(HttpClient);
  private router = inject(Router);

  current = this.settingsService.settings;
  email = this.auth.email;
  themes: { value: Theme; label: string }[] = [
    { value: 'system', label: 'Match my device' },
    { value: 'light', label: 'Light' },
    { value: 'dark', label: 'Dark' },
  ];

  saveNote = signal<string | null>(null);
  saveFailed = signal(false);

  oldPassword = '';
  newPassword = '';
  pwBusy = signal(false);
  pwNote = signal<string | null>(null);
  pwFailed = signal(false);

  deletePassword = '';
  confirmDelete = false;
  deleteBusy = signal(false);
  deleteError = signal<string | null>(null);

  constructor() {
    void this.settingsService.ensureLoaded();
  }

  async update(change: Partial<Settings>) {
    const before = this.current();
    const next = { ...before, ...change };
    if (change.theme) this.settingsService.preview(change.theme); // show it right away
    this.saveFailed.set(false);
    try {
      await this.settingsService.save(next);
      this.saveNote.set('Saved');
    } catch {
      this.settingsService.settings.set(before);
      this.saveFailed.set(true);
      this.saveNote.set("Couldn't save that change. Please try again.");
    }
  }

  async changePassword() {
    this.pwNote.set(null);
    this.pwFailed.set(false);
    if (this.newPassword.length < 12) {
      this.pwFailed.set(true);
      this.pwNote.set('The new password needs at least 12 characters.');
      return;
    }
    this.pwBusy.set(true);
    try {
      // ASP.NET Core Identity's own endpoint: it checks the old password before changing it
      await firstValueFrom(this.http.post('/api/auth/manage/info', { oldPassword: this.oldPassword, newPassword: this.newPassword }));
      this.oldPassword = this.newPassword = '';
      this.pwNote.set('Password changed.');
    } catch (e) {
      this.pwFailed.set(true);
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.pwNote.set(status === 429 ? 'Too many attempts. Wait a few minutes and try again.'
        : status === 400 ? "Check your current password, and that the new one has at least 12 characters."
        : "Couldn't change your password. Please try again.");
    } finally {
      this.pwBusy.set(false);
    }
  }

  async deleteAccount() {
    this.deleteBusy.set(true);
    this.deleteError.set(null);
    try {
      await firstValueFrom(this.http.post('/api/account/delete', { password: this.deletePassword }));
      await this.auth.refresh(); // the server signed us out
      await this.router.navigate(['/login'], { queryParams: { deleted: 1 } });
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.deleteError.set(status === 400 ? "That password isn't correct."
        : status === 429 ? 'Too many attempts. Wait a few minutes and try again.'
        : "Couldn't delete your account. Please try again.");
    } finally {
      this.deleteBusy.set(false);
    }
  }
}

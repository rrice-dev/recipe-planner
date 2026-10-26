import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';

// Optional weekly grocery budget that a meal plan's estimated cost is compared with
@Component({
  selector: 'app-budget-settings',
  template: `
    @if (available()) {
      <section class="card">
        <h2>Weekly grocery budget</h2>
        <p class="small-text muted">Optional. Meal plans compare their estimated cost with this.</p>
        <div class="row">
          <label class="inline-label">$ <input type="number" min="1" step="1" inputmode="decimal" class="narrow" aria-label="Weekly budget in dollars"
                 [value]="budget() ?? ''" (input)="set($any($event.target).value)" /></label>
          <button type="button" (click)="save()" [disabled]="busy()">Save budget</button>
          @if (note()) { <span [class]="failed() ? 'error' : 'saved-note'" role="status">{{ note() }}</span> }
        </div>
      </section>
    }
  `
})
export class BudgetSettingsSection {
  private api = inject(ApiService);
  available = signal(false);
  budget = signal<number | null>(null);
  busy = signal(false);
  note = signal<string | null>(null);
  failed = signal(false);

  constructor() {
    void firstValueFrom(this.api.getKrogerStatus()).then(async s => {
      if (!s.available) return;
      this.available.set(true);
      try { this.budget.set((await firstValueFrom(this.api.getBudget())).weeklyBudget); } catch { /* shows blank */ }
    }).catch(() => {});
  }

  set(raw: string) {
    const n = raw.trim() === '' ? null : Number(raw);
    this.budget.set(n !== null && Number.isFinite(n) ? n : null);
  }

  async save() {
    this.busy.set(true);
    this.note.set(null);
    this.failed.set(false);
    try {
      this.budget.set((await firstValueFrom(this.api.saveBudget(this.budget()))).weeklyBudget);
      this.note.set('Saved');
    } catch {
      this.failed.set(true);
      this.note.set('Enter an amount above $0, or leave it empty.');
    } finally {
      this.busy.set(false);
    }
  }
}

import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { FeaturesService } from './features.service';
import { NutritionTargets } from './models';

type Field = keyof NutritionTargets;

// Optional daily targets that a meal plan's nutrition is compared with. Leave any blank.
@Component({
  selector: 'app-nutrition-targets',
  template: `
    @if (features.features().nutrition) {
      <section class="card">
        <h2>Daily nutrition targets</h2>
        <p class="small-text muted">Optional. Meal plans compare an average planned day with these. Leave any blank.</p>
        <div class="targets-grid">
          @for (f of fields; track f.key) {
            <label>{{ f.label }}
              <span class="with-unit">
                <input type="number" inputmode="numeric" [min]="f.min" [max]="f.max" [value]="draft()[f.key] ?? ''"
                       (input)="set(f.key, $any($event.target).value)" />
                @if (f.unit) { <span class="muted">{{ f.unit }}</span> }
              </span>
            </label>
          }
        </div>
        <div class="row">
          <button type="button" (click)="save()" [disabled]="busy()">Save targets</button>
          @if (note()) { <span [class]="failed() ? 'error' : 'saved-note'" role="status">{{ note() }}</span> }
        </div>
      </section>
    }
  `
})
export class NutritionTargetsSection {
  private api = inject(ApiService);
  readonly features = inject(FeaturesService);

  readonly fields: { key: Field; label: string; unit: string; min: number; max: number }[] = [
    { key: 'calories', label: 'Calories', unit: '', min: 500, max: 10000 },
    { key: 'protein', label: 'Protein', unit: 'g', min: 0, max: 1000 },
    { key: 'carbs', label: 'Carbs', unit: 'g', min: 0, max: 1000 },
    { key: 'fat', label: 'Fat', unit: 'g', min: 0, max: 1000 }
  ];
  draft = signal<NutritionTargets>({ calories: null, protein: null, carbs: null, fat: null });
  busy = signal(false);
  note = signal<string | null>(null);
  failed = signal(false);

  constructor() {
    void this.features.ensureLoaded().then(async f => {
      if (!f.nutrition) return;
      try { this.draft.set(await firstValueFrom(this.api.getNutritionTargets())); } catch { /* shows blank */ }
    });
  }

  set(key: Field, raw: string) {
    const n = raw.trim() === '' ? null : Math.round(Number(raw));
    this.draft.update(d => ({ ...d, [key]: Number.isFinite(n) ? n : null }));
  }

  async save() {
    this.busy.set(true);
    this.note.set(null);
    this.failed.set(false);
    try {
      this.draft.set(await firstValueFrom(this.api.saveNutritionTargets(this.draft())));
      this.note.set('Saved');
    } catch (e) {
      this.failed.set(true);
      const detail = e instanceof HttpErrorResponse && e.status === 400
        ? Object.values((e.error?.errors ?? {}) as Record<string, string[]>).flat()[0] : null;
      this.note.set(detail ?? "Couldn't save your targets. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

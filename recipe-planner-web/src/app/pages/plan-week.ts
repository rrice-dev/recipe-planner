import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { apiErrorMessage } from '../api-errors';
import { SettingsService } from '../settings.service';
import { PlanCandidate } from '../models';
import { nextMonday, parseDate, toIsoDate } from '../date-utils';
import { totalTime } from '../library-utils';

interface PlanDay { iso: string; name: string; date: string; pick: PlanCandidate | null; }

const THEMES = [
  { value: '', label: 'Anything' },
  { value: 'Asian', label: 'Asian' },
  { value: 'Quick', label: 'Quick (30 min or less)' },
  { value: 'Soup', label: 'Soups and stews' },
];

// "Plan my week": suggests dinners from the library and your own recipes, lets you swap any day,
// then saves the plan (and can fill your shopping list).
@Component({
  selector: 'app-plan-week',
  imports: [FormsModule, RouterLink],
  template: `
    <a routerLink="/meal-plans" class="back">← Meal plans</a>
    <h1>Plan my week</h1>
    <p class="muted">Pick what kind of week you want, and we'll suggest a dinner for each day. Swap anything you don't like.</p>

    <div class="card">
      <div class="planner-controls">
        <label>Week starting
          <input type="date" [(ngModel)]="weekStart" (ngModelChange)="rebuildDays()" />
        </label>
        <label>Dinners
          <select [(ngModel)]="count" (ngModelChange)="rebuildDays()">
            @for (n of [3, 4, 5, 6, 7]; track n) { <option [ngValue]="n">{{ n }}</option> }
          </select>
        </label>
        <label>Diet
          <select [(ngModel)]="diet">
            <option [ngValue]="null">No restrictions</option>
            <option ngValue="AIP">AIP</option>
            <option ngValue="Vegetarian">Vegetarian</option>
          </select>
        </label>
        <label>Theme
          <select [(ngModel)]="theme">
            @for (t of themes; track t.value) { <option [ngValue]="t.value">{{ t.label }}</option> }
          </select>
        </label>
        <label>Recipes from
          <select [(ngModel)]="source">
            <option ngValue="both">Library and my recipes</option>
            <option ngValue="library">Library only</option>
            <option ngValue="mine">My recipes only</option>
          </select>
        </label>
        <label>Servings each
          <input type="number" min="1" max="100" class="tiny" [(ngModel)]="servings" />
        </label>
      </div>
      <button type="button" class="primary" (click)="suggest()" [disabled]="busy()">
        {{ busy() ? 'Picking…' : (hasPicks() ? 'Suggest a new week' : 'Suggest a week') }}
      </button>
    </div>

    @if (note()) { <p class="callout" role="status">{{ note() }}</p> }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

    @if (hasPicks()) {
      <ol class="plan-days">
        @for (d of days(); track d.iso) {
          <li class="plan-day" [class.empty]="!d.pick">
            <span class="day-name">{{ d.name }}<span class="day-date">{{ d.date }}</span></span>
            <div class="dish">
              @if (d.pick; as p) {
                <strong>{{ p.title }}</strong>
                <span class="source-mark">{{ p.fromLibrary ? 'From the library' : 'Your recipe' }}{{ p.totalMinutes ? ', ' + time(p.totalMinutes) : '' }}</span>
              } @else {
                <span>Nothing planned</span>
              }
            </div>
            <div class="day-actions">
              <button type="button" (click)="swap(d)" [disabled]="busy()">{{ d.pick ? 'Swap' : 'Pick one' }}</button>
              @if (d.pick) {
                <button type="button" class="link" (click)="clear(d)" [attr.aria-label]="'Remove ' + d.pick.title">Remove</button>
              }
            </div>
          </li>
        }
      </ol>

      <div class="card">
        <label class="field" style="display: flex; flex-direction: column; gap: 0.3rem; font-weight: 700; margin-bottom: 0.9rem">Plan name
          <input [(ngModel)]="name" maxlength="100" />
        </label>
        <p class="small-text muted">Library dinners are added to your recipes so you can cook and edit them. If you already have one, your copy is used.</p>
        <div class="actions">
          <button type="button" class="primary" (click)="save(true)" [disabled]="saving()">Save and add to shopping list</button>
          <button type="button" (click)="save(false)" [disabled]="saving()">Save plan</button>
        </div>
      </div>
    }
  `
})
export class PlanWeekPage {
  private api = inject(ApiService);
  private router = inject(Router);
  private settingsService = inject(SettingsService);
  private settings = this.settingsService.settings;

  themes = THEMES;
  time = totalTime;

  weekStart = nextMonday();
  count = 7;
  diet: string | null = this.settings().diet;
  theme = '';
  source = 'both';
  servings = this.settings().defaultServings;
  name = '';

  days = signal<PlanDay[]>([]);
  busy = signal(false);
  saving = signal(false);
  note = signal<string | null>(null);
  error = signal<string | null>(null);
  hasPicks = computed(() => this.days().some(d => d.pick));

  constructor() {
    this.rebuildDays();
    // Start from your saved diet and servings once they've loaded
    void this.settingsService.ensureLoaded().then(s => {
      if (!this.hasPicks()) { this.diet = s.diet; this.servings = s.defaultServings; }
    });
  }

  /** Keeps picks for days that are still in range when you change the week or number of dinners */
  rebuildDays() {
    const start = parseDate(this.weekStart || nextMonday());
    const old = new Map(this.days().map((d, i) => [i, d.pick]));
    this.days.set(Array.from({ length: this.count }, (_, i) => {
      const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
      return {
        iso: toIsoDate(d),
        name: d.toLocaleDateString(undefined, { weekday: 'long' }),
        date: d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
        pick: old.get(i) ?? null
      };
    }));
    this.name = `Week of ${start.toLocaleDateString(undefined, { month: 'long', day: 'numeric' })}`;
  }

  private request(count: number, exclude: string[]) {
    return firstValueFrom(this.api.suggestMeals({
      count, diet: this.diet, tag: this.theme || null, cuisine: null, source: this.source, exclude,
      useMyDiet: false // the Diet menu above already starts on your setting; "No restrictions" here means none
    }));
  }

  async suggest() {
    await this.run(async () => {
      const res = await this.request(this.count, []);
      this.days.update(days => days.map((d, i) => ({ ...d, pick: res.suggestions[i] ?? null })));
      this.note.set(res.suggestions.length < this.count
        ? `Only ${res.matching} recipe${res.matching === 1 ? '' : 's'} match these choices, so some days are empty. Try another theme, or include the library.`
        : null);
    });
  }

  async swap(day: PlanDay) {
    await this.run(async () => {
      const taken = this.days().map(d => d.pick?.key).filter((k): k is string => !!k);
      const res = await this.request(1, taken);
      if (!res.suggestions.length) {
        this.note.set('No other recipes match these choices. Try another theme, or include the library.');
        return;
      }
      this.days.update(days => days.map(d => d.iso === day.iso ? { ...d, pick: res.suggestions[0] } : d));
    });
  }

  clear(day: PlanDay) {
    this.days.update(days => days.map(d => d.iso === day.iso ? { ...d, pick: null } : d));
  }

  async save(alsoShoppingList: boolean) {
    const picks = this.days().filter(d => d.pick).map(d => ({ day: d.iso, key: d.pick!.key, servings: this.servings }));
    if (!this.name.trim()) { this.error.set('Give the plan a name.'); return; }
    this.saving.set(true);
    this.error.set(null);
    try {
      const plan = await firstValueFrom(this.api.savePlannedWeek(this.name.trim(), this.weekStart, picks));
      if (alsoShoppingList) {
        await firstValueFrom(this.api.addMealPlanToGroceryList(plan.id));
        await this.router.navigate(['/shopping-list']);
      } else {
        await this.router.navigate(['/meal-plans', plan.id]);
      }
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 400
        ? apiErrorMessage(e, 'Check the week and servings, then try again.') : "Couldn't save the plan. Please try again.");
    } finally {
      this.saving.set(false);
    }
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    this.note.set(null);
    try {
      await action();
    } catch {
      this.error.set("Couldn't get suggestions. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

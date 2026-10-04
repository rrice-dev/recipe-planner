import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { MealPlan, RecipeSummary } from '../models';
import { formatWeek, weekDays } from '../date-utils';

interface EntryDraft {
  day: string;
  recipeId: number;
  servings: number;
}

@Component({
  selector: 'app-meal-plan-detail',
  imports: [RouterLink],
  template: `
    <a routerLink="/meal-plans" class="back">← All meal plans</a>
    @if (plan(); as p) {
      <div class="page-header">
        <div>
          <h1>{{ p.name }}</h1>
          <p class="meta">{{ formatWeek(p.weekStart) }}</p>
        </div>
        <div class="row">
          <button class="primary" (click)="save()" [disabled]="saving() || !dirty()">
            {{ saving() ? 'Saving…' : dirty() ? 'Save changes' : 'Saved' }}
          </button>
          <a [routerLink]="['/meal-plans', p.id, 'shopping-list']" class="button"
             [class.disabled]="dirty()">Shopping list</a>
        </div>
      </div>

      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
      @if (dirty()) {
        <p class="muted">Save your changes before opening the shopping list.</p>
      }

      <div class="week">
        @for (day of days(); track day.iso) {
          <section class="day card">
            <h2>{{ day.label }}</h2>

            @for (e of entriesFor(day.iso); track e) {
              <div class="entry">
                <span class="grow">{{ recipeTitle(e.recipeId) }}</span>
                <span class="muted">× {{ e.servings }}</span>
                <button class="link" (click)="remove(e)">Remove</button>
              </div>
            } @empty {
              <p class="muted small-text">Nothing planned</p>
            }

            <div class="row add">
              <select #recipe class="grow">
                <option value="">Add a recipe…</option>
                @for (r of recipes(); track r.id) {
                  <option [value]="r.id">{{ r.title }}</option>
                }
              </select>
              <input #servings type="number" min="1" value="4" class="tiny" title="Servings" />
              <button (click)="add(day.iso, recipe.value, servings.value); recipe.value = ''">Add</button>
            </div>
          </section>
        }
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class MealPlanDetailPage implements OnInit {
  private api = inject(ApiService);

  id = input.required<string>();

  plan = signal<MealPlan | null>(null);
  recipes = signal<RecipeSummary[]>([]);
  entries = signal<EntryDraft[]>([]);
  dirty = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);

  days = computed(() => {
    const p = this.plan();
    return p ? weekDays(p.weekStart) : [];
  });

  formatWeek = formatWeek;

  async ngOnInit() {
    this.recipes.set(await firstValueFrom(this.api.getRecipes()));
    const plan = await firstValueFrom(this.api.getMealPlan(Number(this.id())));
    this.plan.set(plan);
    this.entries.set(plan.entries.map(e => ({ day: e.day, recipeId: e.recipeId, servings: e.servings })));
  }

  entriesFor(day: string) {
    return this.entries().filter(e => e.day === day);
  }

  recipeTitle(id: number) {
    return this.recipes().find(r => r.id === id)?.title ?? 'Unknown recipe';
  }

  add(day: string, recipeId: string, servings: string) {
    const rid = Number(recipeId);
    const s = Number(servings);
    if (!rid) return;
    if (!s || s < 1) {
      this.error.set('Servings must be at least 1.');
      return;
    }
    this.error.set(null);
    this.entries.update(list => [...list, { day, recipeId: rid, servings: s }]);
    this.dirty.set(true);
  }

  remove(entry: EntryDraft) {
    this.entries.update(list => list.filter(e => e !== entry));
    this.dirty.set(true);
  }

  async save() {
    const p = this.plan();
    if (!p) return;
    this.saving.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.setMealPlanEntries(p.id, this.entries()));
      this.dirty.set(false);
    } catch {
      this.error.set('Could not save the plan. Check the API terminal for details.');
    } finally {
      this.saving.set(false);
    }
  }
}
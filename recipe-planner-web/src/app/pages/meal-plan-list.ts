import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { MealPlan } from '../models';
import { formatWeek, nextMonday } from '../date-utils';

@Component({
  selector: 'app-meal-plan-list',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>Meal Plans</h1>

    <div class="card create">
      <h2>Plan a new week</h2>
      <div class="row">
        <input [(ngModel)]="name" placeholder="Name, e.g. Batch cook week" class="grow" />
        <input type="date" [(ngModel)]="weekStart" />
        <button class="primary" (click)="create()">Create</button>
      </div>
      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </div>

    @if (plans(); as list) {
      <div class="grid">
        @for (p of list; track p.id) {
          <div class="card">
            <a [routerLink]="['/meal-plans', p.id]" class="plain">
              <h2>{{ p.name }}</h2>
              <p class="meta">{{ formatWeek(p.weekStart) }} · {{ p.entries.length }} meal(s)</p>
            </a>
            <div class="row">
              <a [routerLink]="['/meal-plans', p.id, 'shopping-list']" class="button">Shopping list</a>
              <button class="link" (click)="remove(p)">Delete</button>
            </div>
          </div>
        } @empty {
          <p class="muted">No meal plans yet. Create one above.</p>
        }
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class MealPlanListPage implements OnInit {
  private api = inject(ApiService);
  private router = inject(Router);

  plans = signal<MealPlan[] | null>(null);
  name = signal('');
  weekStart = signal(nextMonday());
  error = signal<string | null>(null);
  formatWeek = formatWeek;

  async ngOnInit() {
    await this.load();
  }

  async load() {
    this.plans.set(await firstValueFrom(this.api.getMealPlans()));
  }

  async create() {
    const name = this.name().trim();
    if (!name) {
      this.error.set('Give the plan a name.');
      return;
    }
    if (!this.weekStart()) {
      this.error.set('Pick a start date.');
      return;
    }
    try {
      const plan = await firstValueFrom(this.api.createMealPlan(name, this.weekStart()));
      this.router.navigate(['/meal-plans', plan.id]);
    } catch {
      this.error.set('Could not create the plan.');
    }
  }

  async remove(plan: MealPlan) {
    if (!confirm(`Delete "${plan.name}"?`)) return;
    await firstValueFrom(this.api.deleteMealPlan(plan.id));
    await this.load();
  }
}
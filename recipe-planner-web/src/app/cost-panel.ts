import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { KrogerStatus, PlanCost, RecipeCost } from './models';
import { budgetUse, money } from './cost';
import { parseDate } from './date-utils';

// Estimated cost from your Kroger store's prices, for one recipe or a whole meal plan.
// Asks first instead of loading on every visit, because the first estimate searches the store.
@Component({
  selector: 'app-cost-panel',
  imports: [RouterLink],
  template: `
    @if (status(); as s) {
      @if (s.available) {
        <section class="card cost-panel">
          <h2>Cost <span class="muted small-text">estimated from Kroger prices</span></h2>
          @if (!s.locationId) {
            <p class="small-text">Connect Kroger and pick your store on the <a routerLink="/shopping-list">shopping list</a> page to see what {{ planId() ? 'this week' : 'this recipe' }} costs.</p>
          } @else if (recipeCost(); as c) {
            <p class="cost-headline"><strong>{{ money(c.perServing) }}</strong> a serving
              <span class="muted">· {{ money(c.total) }} for {{ c.servings }} at {{ c.storeName }}</span></p>
            <p class="small-text muted">Priced {{ c.counted }} of {{ c.totalIngredients }} ingredients by the amount the recipe uses, not whole packages.</p>
            <details>
              <summary class="small-text">How each ingredient was priced</summary>
              <ul class="small-text cost-lines">
                @for (l of c.lines; track l.ingredient) {
                  <li><strong>{{ l.ingredient }}</strong>&nbsp;
                    @if (l.cost !== null) {
                      {{ money(l.cost) }}{{ l.approximate ? ' (approx.)' : '' }}
                      <span class="muted">· {{ l.product }}, {{ l.size }} for {{ money(l.packagePrice) }}</span>
                    } @else if (l.product) {
                      <span class="muted"> · couldn't match the recipe's amount to {{ l.product }} ({{ l.size ?? 'no size listed' }})</span>
                    } @else {
                      <span class="muted"> · not found at this store</span>
                    }
                  </li>
                }
              </ul>
            </details>
          } @else if (planCost(); as c) {
            <div class="table-scroll">
              <table class="nutrition-table cost-table">
                <thead><tr><th scope="col">Meal</th><th scope="col">Servings</th><th scope="col">Per serving</th><th scope="col">Cost</th></tr></thead>
                <tbody>
                  @for (m of c.meals; track $index) {
                    <tr><th scope="row">{{ m.title }}<span class="muted small-text meals">{{ weekday(m.day) }}{{ m.complete ? '' : ' · some ingredients not priced' }}</span></th>
                      <td>{{ m.servings }}</td><td>{{ money(m.perServing) }}</td><td>{{ money(m.cost) }}</td></tr>
                  }
                </tbody>
                <tfoot><tr><th scope="row">Week total</th><td></td><td></td><td>{{ money(c.total) }}</td></tr></tfoot>
              </table>
            </div>
            @if (budget(); as b) {
              <div class="meter budget-meter" [class.over]="b.over">
                <div class="meter-label"><strong>Weekly budget</strong>
                  <span>{{ money(c.total) }} of {{ money(c.budget) }} · {{ b.over ? money(-b.left) + ' over' : money(b.left) + ' left' }}</span></div>
                <div class="meter-track" role="presentation"><span [style.width.%]="b.percent > 100 ? 100 : b.percent"></span></div>
              </div>
            } @else {
              <p class="small-text muted">Set a weekly budget in <a routerLink="/settings">Settings</a> to compare.</p>
            }
            <p class="small-text muted">At {{ c.storeName }}, by the amount each meal uses. Pantry staples you already have are included, so your receipt may be lower.</p>
          } @else {
            <button type="button" (click)="estimate()" [disabled]="busy()">{{ busy() ? 'Checking prices…' : (planId() ? "Estimate this week's cost" : 'Estimate cost') }}</button>
            <span class="small-text muted"> at {{ s.locationName }}</span>
          }
          @if (error()) { <p class="error small-text" role="alert">{{ error() }}</p> }
        </section>
      }
    }
  `
})
export class CostPanel {
  private api = inject(ApiService);

  recipeId = input<number | null>(null);
  planId = input<number | null>(null);
  servings = input<number | null>(null);

  status = signal<KrogerStatus | null>(null);
  recipeCost = signal<RecipeCost | null>(null);
  planCost = signal<PlanCost | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);
  money = money;
  budget = computed(() => { const c = this.planCost(); return c ? budgetUse(c.total, c.budget) : null; });

  constructor() {
    void firstValueFrom(this.api.getKrogerStatus()).then(s => this.status.set(s)).catch(() => {});
  }

  weekday(iso: string) { return parseDate(iso).toLocaleDateString(undefined, { weekday: 'long' }); }

  async estimate() {
    this.busy.set(true);
    this.error.set(null);
    try {
      const plan = this.planId(), recipe = this.recipeId();
      if (plan) this.planCost.set(await firstValueFrom(this.api.getPlanCost(plan)));
      else if (recipe) this.recipeCost.set(await firstValueFrom(this.api.getRecipeCost(recipe, this.servings())));
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 429 ? 'Too many price checks. Try again in a minute.'
        : e instanceof HttpErrorResponse && e.status === 502 ? "Kroger isn't responding right now. Try again in a minute."
        : "Couldn't get prices. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

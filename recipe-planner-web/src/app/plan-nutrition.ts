import { Component, effect, inject, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { FeaturesService } from './features.service';
import { Nutrients, NutritionTargets, PlanNutrition } from './models';
import { parseDate } from './date-utils';

export interface TargetMeter { label: string; value: number; target: number; unit: string; percent: number }

// Average vs target, for the targets that are set. Percent is capped for the bar, not the text.
export function targetMeters(average: Nutrients, targets: NutritionTargets): TargetMeter[] {
  const rows: [string, number, number | null, string][] = [
    ['Calories', average.calories, targets.calories, ''],
    ['Protein', average.protein, targets.protein, ' g'],
    ['Carbs', average.carbs, targets.carbs, ' g'],
    ['Fat', average.fat, targets.fat, ' g']
  ];
  return rows.filter(([, , t]) => t !== null && t > 0)
    .map(([label, value, target, unit]) => ({ label, value, target: target!, unit, percent: Math.round(value / target! * 100) }));
}

// One serving of each planned meal, day by day, and how the average day compares with your targets
@Component({
  selector: 'app-plan-nutrition',
  imports: [DecimalPipe, RouterLink],
  template: `
    @if (data(); as n) {
      @if (n.available && n.days.length) {
        <section class="card plan-nutrition">
          <h2>Nutrition <span class="muted small-text">one serving of each meal, estimated</span></h2>
          <div class="table-scroll">
            <table class="nutrition-table">
              <thead><tr><th scope="col">Day</th><th scope="col">Calories</th><th scope="col">Protein</th><th scope="col">Carbs</th><th scope="col">Fat</th></tr></thead>
              <tbody>
                @for (d of n.days; track d.day) {
                  <tr>
                    <th scope="row">{{ weekday(d.day) }}
                      <span class="muted small-text meals">{{ titles(d) }}</span></th>
                    <td>{{ d.total.calories | number }}</td><td>{{ d.total.protein | number:'1.0-0' }} g</td>
                    <td>{{ d.total.carbs | number:'1.0-0' }} g</td><td>{{ d.total.fat | number:'1.0-0' }} g</td>
                  </tr>
                }
              </tbody>
              @if (n.dailyAverage; as a) {
                <tfoot><tr><th scope="row">Daily average</th><td>{{ a.calories | number }}</td><td>{{ a.protein | number:'1.0-0' }} g</td>
                  <td>{{ a.carbs | number:'1.0-0' }} g</td><td>{{ a.fat | number:'1.0-0' }} g</td></tr></tfoot>
              }
            </table>
          </div>

          @if (n.dailyAverage && meters(n).length) {
            <h3>Your daily targets</h3>
            <p class="small-text muted">How an average planned day compares. If you've only planned dinners, the rest of your day makes up the difference.</p>
            <div class="meters">
              @for (m of meters(n); track m.label) {
                <div class="meter">
                  <div class="meter-label"><strong>{{ m.label }}</strong>
                    <span>{{ m.value | number:'1.0-0' }}{{ m.unit }} of {{ m.target | number }}{{ m.unit }} ({{ m.percent }}%)</span></div>
                  <div class="meter-track" role="presentation"><span [style.width.%]="m.percent > 100 ? 100 : m.percent"></span></div>
                </div>
              }
            </div>
          } @else {
            <p class="small-text muted">Set daily targets in <a routerLink="/settings">Settings</a> to compare your week.</p>
          }
          @if (!n.complete) {
            <p class="small-text muted">Some ingredients couldn't be counted, so these numbers are a little low. Each recipe's page shows which.</p>
          }
        </section>
      }
    }
  `
})
export class PlanNutritionPanel {
  private api = inject(ApiService);
  private features = inject(FeaturesService);

  planId = input.required<number>();
  // Bump to reload after the plan is saved
  version = input(0);
  data = signal<PlanNutrition | null>(null);

  constructor() {
    effect(() => {
      const id = this.planId();
      this.version();
      void this.load(id);
    });
  }

  private async load(id: number) {
    const f = await this.features.ensureLoaded();
    if (!f.nutrition) return;
    try {
      this.data.set(await firstValueFrom(this.api.getPlanNutrition(id)));
    } catch {
      this.data.set(null); // optional extra; the plan works without it
    }
  }

  weekday(iso: string) { return parseDate(iso).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }); }
  titles(d: PlanNutrition['days'][number]) { return d.meals.map(m => m.title).join(', '); }
  meters(n: PlanNutrition) { return n.dailyAverage ? targetMeters(n.dailyAverage, n.targets) : []; }
}

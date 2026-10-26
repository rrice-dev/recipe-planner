import { Component, OnInit, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { FeaturesService } from './features.service';

interface Nutrition {
  available: boolean;
  servings: number;
  perServing: { calories: number; protein: number; carbs: number; fat: number; fiber: number; sugar: number; sodiumMg: number } | null;
  counted: number;
  total: number;
  lines: { ingredient: string; matchedFood: string | null; grams: number | null; calories: number; counted: boolean; approximate: boolean }[];
}

// Estimated nutrition per serving, from USDA FoodData Central. Shows which ingredients it could count.
@Component({
  selector: 'app-nutrition-panel',
  template: `
    @if (data(); as n) {
      @if (n.available && n.perServing; as p) {
        <section class="card nutrition">
          <h2>Nutrition <span class="muted small-text">per serving, estimated</span></h2>
          <dl class="facts nutrition-facts">
            <div><dt>Calories</dt><dd>{{ p.calories }}</dd></div>
            <div><dt>Protein</dt><dd>{{ p.protein }} g</dd></div>
            <div><dt>Carbs</dt><dd>{{ p.carbs }} g</dd></div>
            <div><dt>Fat</dt><dd>{{ p.fat }} g</dd></div>
            <div><dt>Fiber</dt><dd>{{ p.fiber }} g</dd></div>
            <div><dt>Sugar</dt><dd>{{ p.sugar }} g</dd></div>
            <div><dt>Sodium</dt><dd>{{ p.sodiumMg }} mg</dd></div>
          </dl>
          <p class="small-text muted">
            Counted {{ n.counted }} of {{ n.total }} ingredients, for {{ n.servings }} servings as written, using USDA FoodData Central.
            Amounts measured by volume are converted using typical weights, so treat this as a guide.
          </p>
          <details>
            <summary class="small-text">How each ingredient was counted</summary>
            <ul class="small-text">
              @for (l of n.lines; track l.ingredient) {
                <li>
                  <strong>{{ l.ingredient }}</strong>:
                  @if (l.counted) {
                    {{ l.grams }} g as "{{ l.matchedFood }}", {{ l.calories }} kcal@if (l.approximate) { (approximate weight)}
                  } @else if (l.matchedFood) {
                    not counted (no weight for this unit)
                  } @else {
                    not counted (no USDA match)
                  }
                </li>
              }
            </ul>
          </details>
        </section>
      }
    }
  `
})
export class NutritionPanel implements OnInit {
  private http = inject(HttpClient);
  private features = inject(FeaturesService);
  /** e.g. /api/recipes/5/nutrition or /api/library/slug/nutrition */
  url = input.required<string>();
  data = signal<Nutrition | null>(null);

  async ngOnInit() {
    if (!(await this.features.ensureLoaded()).nutrition) return;
    try { this.data.set(await firstValueFrom(this.http.get<Nutrition>(this.url()))); } catch { /* optional: just don't show it */ }
  }
}

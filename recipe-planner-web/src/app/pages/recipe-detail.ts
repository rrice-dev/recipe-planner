import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-recipe-detail',
  imports: [RouterLink],
  template: `
    <a routerLink="/recipes" class="back">← All recipes</a>
    @if (recipe(); as r) {
      <h1>{{ r.title }}</h1>
      <p class="meta">
        {{ r.cuisine ?? 'Any cuisine' }} · Prep {{ r.prepMinutes }} min · Cook {{ r.cookMinutes }} min · Serves {{ r.servings }}
      </p>
      @if (r.description) {
        <p>{{ r.description }}</p>
      }

      @if (r.techniques.length) {
        <h2>Techniques used</h2>
        <div class="chips">
          @for (t of r.techniques; track t.id) {
            <span class="chip">{{ t.name }} · {{ t.difficulty }}/5</span>
          }
        </div>
      }

      <h2>Ingredients</h2>
      @if (r.ingredients.length) {
        <ul>
          @for (i of r.ingredients; track i.ingredientId) {
            <li>
              {{ i.quantity }} {{ i.unit }} {{ i.name }}
              @if (i.note) { <span class="muted">({{ i.note }})</span> }
            </li>
          }
        </ul>
      } @else {
        <p class="muted">No ingredients added yet.</p>
      }

      <h2>Steps</h2>
      @if (r.steps.length) {
        <ol>
          @for (s of r.steps; track s.order) {
            <li>
              {{ s.instruction }}
              @if (s.techniqueName) { <span class="chip small">{{ s.techniqueName }}</span> }
            </li>
          }
        </ol>
      } @else {
        <p class="muted">No steps added yet.</p>
      }
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class RecipeDetailPage {
  private api = inject(ApiService);
  id = input.required<string>();
  recipe = toSignal(toObservable(this.id).pipe(switchMap(id => this.api.getRecipe(Number(id)))));
}
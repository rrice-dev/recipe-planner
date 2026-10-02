import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-recipe-list',
  imports: [RouterLink],
  template: `
    <h1>Recipes</h1>
    @if (recipes(); as list) {
      <div class="grid">
        @for (r of list; track r.id) {
          <a class="card" [routerLink]="['/recipes', r.id]">
            <h2>{{ r.title }}</h2>
            <p class="meta">
              {{ r.cuisine ?? 'Any cuisine' }} · {{ r.prepMinutes + r.cookMinutes }} min · serves {{ r.servings }}
            </p>
            @if (r.description) {
              <p>{{ r.description }}</p>
            }
          </a>
        } @empty {
          <p class="muted">No recipes yet.</p>
        }
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class RecipeListPage {
  private api = inject(ApiService);
  recipes = toSignal(this.api.getRecipes());
}
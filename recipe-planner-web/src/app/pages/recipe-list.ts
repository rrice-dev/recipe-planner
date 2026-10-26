import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-recipe-list',
  imports: [RouterLink],
  template: `
    <div class="page-header">
  <h1>Recipes</h1>
  <a routerLink="/recipes/new" class="button primary">New recipe</a>
</div>
    @if (recipes(); as list) {
      <div class="grid">
        @for (r of list; track r.id) {
          <a class="card" [routerLink]="['/recipes', r.id]">
            @if (r.photoVersion) {
              <img class="card-photo" [src]="'/api/recipes/' + r.id + '/photo?v=' + r.photoVersion" alt="" loading="lazy" />
            } @else {
              <div class="card-band" aria-hidden="true"></div>
            }
            <h2>{{ r.title }}</h2>
            <p class="meta small-text">{{ r.prepMinutes + r.cookMinutes }} min, serves {{ r.servings }}@if (r.cuisine) {, {{ r.cuisine }}}</p>
            @if (r.description) {
              <p>{{ r.description }}</p>
            }
          </a>
        } @empty {
          <div class="card">
            <h2>No recipes yet</h2>
            <p>Add one from the <a routerLink="/library">recipe library</a>, or write your own.</p>
          </div>
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
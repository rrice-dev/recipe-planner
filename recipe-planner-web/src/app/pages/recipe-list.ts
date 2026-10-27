import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { Collection, RecipeSummary } from '../models';
import { RecipeFilter, RecipeSort, searchRecipes } from '../recipe-search';
import { totalTime } from '../library-utils';

const FILTERS: { value: RecipeFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'favorites', label: 'Favorites' },
  { value: 'AIP', label: 'AIP' },
  { value: 'Vegetarian', label: 'Vegetarian' },
  { value: 'quick', label: 'Quick' },
  { value: 'untried', label: 'Not cooked yet' },
];

@Component({
  selector: 'app-recipe-list',
  imports: [RouterLink],
  template: `
    <div class="page-header">
      <h1>Recipes</h1>
      <div class="row" style="margin: 0">
        <a routerLink="/recipes/import" class="button">Import</a>
        <a routerLink="/recipes/import" [queryParams]="{ mode: 'photo' }" class="button">📷 Scan a recipe</a>
        <a routerLink="/recipes/new" class="button primary">New recipe</a>
      </div>
    </div>

    @if (recipes(); as all) {
      @if (all.length) {
        <div class="library-filters">
          <input type="search" placeholder="Search by name, cuisine or ingredient" aria-label="Search your recipes"
                 [value]="text()" (input)="text.set($any($event.target).value)" />
          <select aria-label="Collection" [value]="collectionId() ?? ''" (change)="collectionId.set(+$any($event.target).value || null)">
            <option value="">All collections</option>
            @for (c of collections(); track c.id) { <option [value]="c.id">{{ c.name }} ({{ c.recipeCount }})</option> }
          </select>
          <select aria-label="Sort" [value]="sort()" (change)="sort.set($any($event.target).value)">
            <option value="title">A to Z</option>
            <option value="recent">Recently cooked</option>
            <option value="most">Most cooked</option>
            <option value="quickest">Quickest</option>
          </select>
        </div>
        <div class="chips filter-chips" role="group" aria-label="Filter">
          @for (f of filters; track f.value) {
            <button type="button" class="chip-button" [class.selected]="filter() === f.value" [attr.aria-pressed]="filter() === f.value"
                    (click)="filter.set(f.value)">{{ f.label }}</button>
          }
        </div>
        <p class="muted small-text">{{ shown().length }} of {{ all.length }} recipes</p>
      }

      <div class="grid">
        @for (r of shown(); track r.id) {
          <div class="card recipe-card">
            <a class="plain" [routerLink]="['/recipes', r.id]">
              @if (r.photoVersion) {
                <img class="card-photo" [src]="'/api/recipes/' + r.id + '/photo?v=' + r.photoVersion" alt="" loading="lazy" />
              } @else {
                <div class="card-band" aria-hidden="true"></div>
              }
              <h2>{{ r.title }}</h2>
              <p class="meta small-text">{{ time(r.prepMinutes + r.cookMinutes) }}, serves {{ r.servings }}@if (r.cuisine) {, {{ r.cuisine }}}</p>
              @if (r.description) { <p class="small-text">{{ r.description }}</p> }
              @if (r.timesCooked) {
                <p class="small-text cooked-note">Cooked {{ r.timesCooked === 1 ? 'once' : r.timesCooked + ' times' }}, last {{ date(r.lastCooked!) }}</p>
              }
            </a>
            <button type="button" class="fav-toggle" [class.on]="r.isFavorite" (click)="toggleFavorite(r)"
                    [attr.aria-pressed]="!!r.isFavorite" [attr.aria-label]="(r.isFavorite ? 'Remove ' : 'Add ') + r.title + (r.isFavorite ? ' from' : ' to') + ' favorites'">
              {{ r.isFavorite ? '★' : '☆' }}
            </button>
          </div>
        } @empty {
          @if (all.length) {
            <p class="muted">No recipes match. Try a different search or filter.</p>
          } @else {
            <div class="card">
              <h2>No recipes yet</h2>
              <p>Add one from the <a routerLink="/library">recipe library</a>, or write your own.</p>
            </div>
          }
        }
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class RecipeListPage {
  private api = inject(ApiService);
  filters = FILTERS;
  time = totalTime;

  recipes = signal<RecipeSummary[] | null>(null);
  collections = signal<Collection[]>([]);
  text = signal('');
  filter = signal<RecipeFilter>('all');
  collectionId = signal<number | null>(null);
  sort = signal<RecipeSort>('title');

  shown = computed(() => searchRecipes(this.recipes() ?? [], {
    text: this.text(), filter: this.filter(), collectionId: this.collectionId(), sort: this.sort()
  }));

  constructor() {
    void this.load();
  }

  private async load() {
    const [recipes, collections] = await Promise.all([
      firstValueFrom(this.api.getRecipes()),
      firstValueFrom(this.api.getCollections()).catch(() => [] as Collection[]),
    ]);
    this.recipes.set(recipes);
    this.collections.set(collections);
  }

  date(iso: string) {
    const [y, m, d] = iso.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }

  // Updates right away; puts it back if saving fails
  async toggleFavorite(r: RecipeSummary) {
    const next = !r.isFavorite;
    const update = (value: boolean) => this.recipes.update(list => list!.map(x => x.id === r.id ? { ...x, isFavorite: value } : x));
    update(next);
    try {
      await firstValueFrom(this.api.setFavorite(r.id, next));
    } catch {
      update(!next);
    }
  }
}

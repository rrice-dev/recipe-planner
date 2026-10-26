import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';
import { ApiService } from '../api.service';
import { SettingsService } from '../settings.service';
import { LibrarySummary } from '../models';
import { cuisineFamily, totalTime } from '../library-utils';

type LoadState = { status: 'loading' | 'ok' | 'error'; recipes: LibrarySummary[] };

const FILTER_TAGS = ['All', 'Asian', 'AIP', 'Vegetarian', 'Quick', 'Soup', 'Breakfast', 'Side', 'Dessert'];

// Built-in recipes anyone can browse. "Add to my recipes" on the detail page copies one into your book.
@Component({
  selector: 'app-library-list',
  imports: [RouterLink],
  template: `
    <div class="page-header">
      <h1>Recipe Library</h1>
      <a routerLink="/world" class="button">🌍 World map</a>
    </div>
    <p class="muted">Browse recipes, then add any of them to your own book, where you can change them however you like.</p>

    <div class="library-filters">
      <input type="search" placeholder="Search dishes or ingredients" aria-label="Search recipes"
             [value]="search()" (input)="search.set($any($event.target).value)" />
      <select aria-label="Cuisine" [value]="cuisine()" (change)="cuisine.set($any($event.target).value)">
        <option value="">All cuisines</option>
        @for (c of cuisines(); track c) {
          <option [value]="c">{{ c }}</option>
        }
      </select>
    </div>
    <div class="chips filter-chips" role="group" aria-label="Filter by tag">
      @for (t of tags; track t) {
        <button type="button" class="chip-button" [class.selected]="tag() === t" [attr.aria-pressed]="tag() === t"
                (click)="tag.set(t)">{{ t }}</button>
      }
    </div>

    @if (tag() === 'AIP') {
      <p class="callout small-text">
        AIP recipes follow the elimination phase of the Autoimmune Protocol: no grains, legumes, dairy, eggs, nuts, seeds,
        nightshades or refined sugar. Check labels on fish sauce, broth and coconut aminos for hidden sugar or spices.
        This isn't medical advice, so follow your own provider's guidance.
      </p>
    }

    @switch (state().status) {
      @case ('loading') { <p class="muted">Loading…</p> }
      @case ('error') { <p class="error">Couldn't load the library. Please try again.</p> }
      @case ('ok') {
        <p class="muted small-text">{{ filtered().length }} {{ filtered().length === 1 ? 'recipe' : 'recipes' }}</p>
        <div class="grid">
          @for (r of filtered(); track r.slug) {
            <a class="card" [routerLink]="['/library', r.slug]">
              <h2>{{ r.title }}</h2>
              <p class="meta small-text">{{ time(r.prepMinutes + r.cookMinutes) }}, serves {{ r.servings }}, {{ r.cuisine }}</p>
              <p class="small-text">{{ r.description }}</p>
              <div class="chips">
                @for (t of r.tags; track t) {
                  <span class="chip small tag-{{ t.toLowerCase() }}">{{ t }}</span>
                }
              </div>
            </a>
          } @empty {
            <p class="muted">No recipes match. Try a different search or filter.</p>
          }
        </div>
      }
    }
  `
})
export class LibraryListPage {
  private api = inject(ApiService);
  tags = FILTER_TAGS;
  time = totalTime;

  search = signal('');
  // Starts on your diet from Settings (once they load); picking a tag yourself overrides it
  private settings = inject(SettingsService).settings;
  tag = linkedSignal<string>(() => this.settings().diet ?? 'All');
  cuisine = signal('');

  state = toSignal(
    this.api.getLibrary().pipe(
      map((recipes): LoadState => ({ status: 'ok', recipes })),
      catchError(() => of<LoadState>({ status: 'error', recipes: [] }))),
    { initialValue: { status: 'loading', recipes: [] } as LoadState });

  cuisines = computed(() =>
    [...new Set(this.state().recipes.map(r => cuisineFamily(r.cuisine)))].sort());

  filtered = computed(() => {
    const words = this.search().toLowerCase().split(/\s+/).filter(Boolean);
    const tag = this.tag(), cuisine = this.cuisine();
    return this.state().recipes.filter(r =>
      (tag === 'All' || r.tags.includes(tag)) &&
      (!cuisine || cuisineFamily(r.cuisine) === cuisine) &&
      words.every(w => `${r.title} ${r.description} ${r.cuisine}`.toLowerCase().includes(w)));
  });
}

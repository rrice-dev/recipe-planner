import { Component, inject, input, linkedSignal, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, firstValueFrom, map, of, switchMap } from 'rxjs';
import { ApiService } from '../api.service';
import { SettingsService } from '../settings.service';
import { AuthService } from '../auth.service';
import { LibraryDetail } from '../models';
import { totalTime } from '../library-utils';
import { formatMeasure, scaleQuantity } from '../scaling';
import { ServingsStepper } from '../servings-stepper';
import { NutritionPanel } from '../nutrition-panel';

type LoadState = { status: 'loading' } | { status: 'ok'; recipe: LibraryDetail } | { status: 'missing' } | { status: 'error' };

@Component({
  selector: 'app-library-detail',
  imports: [RouterLink, ServingsStepper, NutritionPanel],
  template: `
    <a routerLink="/library" class="back no-print">← Recipe Library</a>
    @switch (state().status) {
      @case ('loading') { <p class="muted">Loading…</p> }
      @case ('missing') {
        <h1>Recipe not found</h1>
        <p class="muted">It may have been renamed. <a routerLink="/library">Browse the library</a>.</p>
      }
      @case ('error') { <p class="error">Couldn't load this recipe. Please try again.</p> }
      @case ('ok') {
        @if (recipe(); as r) {
          <div class="page-header">
            <h1>{{ r.title }}</h1>
            <button type="button" class="no-print" (click)="print()">🖨 Print</button>
          </div>
          <dl class="facts">
            <div><dt>Cuisine</dt><dd>{{ r.cuisine }}</dd></div>
            <div><dt>Prep</dt><dd>{{ time(r.prepMinutes) }}</dd></div>
            <div><dt>Cook</dt><dd>{{ time(r.cookMinutes) }}</dd></div>
            <div><dt>Serves</dt><dd>{{ r.servings }}</dd></div>
          </dl>
          <div class="chips">
            @for (t of r.tags; track t) {
              <span class="chip small tag-{{ t.toLowerCase() }}">{{ t }}</span>
            }
          </div>
          <p>{{ r.description }}</p>

          <div class="card social no-print">
            @if (auth.isLoggedIn()) {
              @if (addedId(); as id) {
                <p class="small-text">Added to your recipes. <a [routerLink]="['/recipes', id]">Open your copy</a></p>
              } @else {
                <div class="row">
                  <button type="button" class="primary" (click)="add(r.slug)" [disabled]="busy()">
                    {{ busy() ? 'Adding…' : 'Add to my recipes' }}
                  </button>
                  <a [routerLink]="['/library', r.slug, 'cook']" class="button">Start cooking</a>
                </div>
                <p class="muted small-text">You get your own copy to edit, rate, share and add to meal plans.</p>
              }
            } @else {
              <div class="row">
                <a [routerLink]="['/library', r.slug, 'cook']" class="button primary">Start cooking</a>
              </div>
              <p class="small-text">
                <a [routerLink]="['/login']" [queryParams]="{ returnUrl: '/library/' + r.slug }">Log in</a>
                to add this to your recipes.
              </p>
            }
            @if (error()) { <p class="error">{{ error() }}</p> }
          </div>

          @if (r.tags.includes('AIP')) {
            <p class="callout small-text">
              AIP elimination-phase friendly. Check labels on fish sauce, broth and coconut aminos for added sugar or
              spices. Not medical advice.
            </p>
          }

          <h2>Ingredients
            @if (servings() !== null && servings() !== r.servings) { <span class="print-only servings-note">(for {{ servings() }} servings)</span> }
          </h2>
          <app-servings-stepper class="no-print" [value]="servings() ?? r.servings" [base]="r.servings" (valueChange)="servings.set($event)" />
          <ul>
            @for (i of r.ingredients; track i.name) {
              <li>
                {{ measure(scale(i.quantity, r.servings), i.unit) }} {{ i.name }}
                @if (i.note) { <span class="muted">({{ i.note }})</span> }
              </li>
            }
          </ul>

          <app-nutrition-panel class="no-print" [url]="'/api/library/' + r.slug + '/nutrition'" />

          <h2>Steps</h2>
          <ol>
            @for (s of r.steps; track s.order) {
              <li class="library-step">
                {{ s.instruction }}
                @if (s.technique) { <a class="chip small" routerLink="/techniques">{{ s.technique }}</a> }
              </li>
            }
          </ol>
        }
      }
    }
  `
})
export class LibraryDetailPage {
  private api = inject(ApiService);

  print() {
    window.print();
  }
  private router = inject(Router);
  auth = inject(AuthService);
  slug = input.required<string>();
  time = totalTime;
  private unitSystem = inject(SettingsService).settings;
  // Amounts in your chosen units (Settings → Units)
  measure = (quantity: number, unit: string) => formatMeasure(quantity, unit, this.unitSystem().units);

  state = toSignal(
    toObservable(this.slug).pipe(
      switchMap(slug => this.api.getLibraryRecipe(slug).pipe(
        map(recipe => ({ status: 'ok', recipe }) as LoadState),
        catchError((e: unknown) =>
          of({ status: e instanceof HttpErrorResponse && e.status === 404 ? 'missing' : 'error' } as LoadState))))),
    { initialValue: { status: 'loading' } as LoadState });

  recipe = () => { const s = this.state(); return s.status === 'ok' ? s.recipe : null; };
  // Starts at the recipe's own servings; resets when you open a different recipe
  servings = linkedSignal<LoadState, number | null>({ source: this.state, computation: () => null });

  scale(quantity: number, base: number) {
    return scaleQuantity(quantity, base, this.servings() ?? base);
  }
  busy = signal(false);
  error = signal<string | null>(null);
  addedId = signal<number | null>(null);

  constructor() {
    void this.auth.ensureLoaded();
  }

  async add(slug: string) {
    this.busy.set(true);
    this.error.set(null);
    try {
      const { recipeId } = await firstValueFrom(this.api.addLibraryRecipe(slug));
      this.addedId.set(recipeId);
      await this.router.navigate(['/recipes', recipeId]);
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 429
        ? "You're adding recipes quickly. Wait a minute and try again."
        : "Couldn't add this recipe. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

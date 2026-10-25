import { Component, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, firstValueFrom, map, of, switchMap } from 'rxjs';
import { ApiService } from '../api.service';
import { AuthService } from '../auth.service';
import { SharedRecipe } from '../models';
import { StarRating } from '../star-rating';

type LoadState = { status: 'loading' } | { status: 'ok'; recipe: SharedRecipe } | { status: 'missing' } | { status: 'error' };

// Public page for a shared link: no login needed to read it
@Component({
  selector: 'app-shared-recipe',
  imports: [RouterLink, StarRating],
  template: `
    @switch (state().status) {
      @case ('loading') {
        <p class="muted">Loading…</p>
      }
      @case ('missing') {
        <h1>Recipe not found</h1>
        <p class="muted">This link doesn't work anymore. The owner may have stopped sharing it.</p>
      }
      @case ('error') {
        <p class="error">Couldn't load this recipe. Please try again.</p>
      }
      @case ('ok') {
        @if (recipe(); as r) {
          <p class="muted small-text">Shared recipe</p>
          <h1>{{ r.title }}</h1>
          <p class="meta">
            {{ r.cuisine ?? 'Any cuisine' }} · Prep {{ r.prepMinutes }} min · Cook {{ r.cookMinutes }} min · Serves {{ r.servings }}
          </p>
          @if (r.description) {
            <p>{{ r.description }}</p>
          }
          @if (r.photoVersion) {
            <img class="recipe-photo" [src]="'/api/shared/' + token() + '/photo?v=' + r.photoVersion" [alt]="'Photo of ' + r.title" />
          }

          <section class="card social">
            <div class="rating-row">
              <strong>Rating</strong>
              @if (r.rating.count) {
                <app-star-rating [value]="r.rating.average" [readonly]="true" label="Community rating" />
                <span class="muted">{{ r.rating.average }} from {{ r.rating.count }} {{ r.rating.count === 1 ? 'rating' : 'ratings' }}</span>
              } @else {
                <span class="muted">No ratings yet</span>
              }
            </div>
            @if (auth.isLoggedIn()) {
              <div class="rating-row">
                <strong>{{ r.rating.isOwner ? 'Your private rating' : 'Your rating' }}</strong>
                <app-star-rating [value]="r.rating.myRating" [disabled]="busy()" label="Your rating" (rate)="rate($event)" />
                @if (r.rating.myRating) {
                  <button type="button" class="link" (click)="clear()" [disabled]="busy()">Clear</button>
                }
              </div>
              @if (r.rating.isOwner) {
                <p class="muted small-text">This is your recipe, so your stars don't count toward the average.</p>
              }
            } @else {
              <p class="small-text"><a [routerLink]="['/login']" [queryParams]="{ returnUrl: '/r/' + token() }">Log in</a> to rate this recipe.</p>
            }
            @if (error()) {
              <p class="error">{{ error() }}</p>
            }
          </section>

          @if (r.techniques.length) {
            <h2>Techniques used</h2>
            <div class="chips">
              @for (t of r.techniques; track t) {
                <span class="chip">{{ t }}</span>
              }
            </div>
          }

          <h2>Ingredients</h2>
          <ul>
            @for (i of r.ingredients; track i.name) {
              <li>
                {{ i.quantity }} {{ i.unit }} {{ i.name }}
                @if (i.note) { <span class="muted">({{ i.note }})</span> }
              </li>
            } @empty {
              <li class="muted plain-item">No ingredients listed.</li>
            }
          </ul>

          <h2>Steps</h2>
          <ol>
            @for (s of r.steps; track s.order) {
              <li>
                {{ s.instruction }}
                @if (s.techniqueName) { <span class="chip small">{{ s.techniqueName }}</span> }
              </li>
            } @empty {
              <li class="muted plain-item">No steps listed.</li>
            }
          </ol>
        }
      }
    }
  `
})
export class SharedRecipePage {
  private api = inject(ApiService);
  auth = inject(AuthService);
  token = input.required<string>();

  state = toSignal(
    toObservable(this.token).pipe(
      switchMap(token => this.api.getSharedRecipe(token).pipe(
        map(recipe => ({ status: 'ok', recipe }) as LoadState),
        catchError((e: unknown) =>
          of({ status: e instanceof HttpErrorResponse && e.status === 404 ? 'missing' : 'error' } as LoadState))))),
    { initialValue: { status: 'loading' } as LoadState });

  recipe = linkedSignal(() => { const s = this.state(); return s.status === 'ok' ? s.recipe : null; });
  busy = signal(false);
  error = signal<string | null>(null);

  constructor() {
    void this.auth.ensureLoaded(); // find out whether to show the rating stars or the "log in" link
  }

  async rate(stars: number) {
    await this.update(async () => {
      await firstValueFrom(this.api.rateSharedRecipe(this.token(), stars));
    });
  }

  async clear() {
    await this.update(async () => {
      await firstValueFrom(this.api.clearSharedRating(this.token()));
    });
  }

  // After rating, reload the summary so the average and count are exact
  private async update(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      const fresh = await firstValueFrom(this.api.getSharedRecipe(this.token()));
      this.recipe.set(fresh);
    } catch {
      this.error.set("Couldn't save your rating. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

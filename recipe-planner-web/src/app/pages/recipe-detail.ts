import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { firstValueFrom, switchMap } from 'rxjs';
import { ApiService } from '../api.service';
import { StarRating } from '../star-rating';
import { shareTargets } from '../share-links';
import { HttpErrorResponse } from '@angular/common/http';
import { PhotoReadError, shrinkPhoto } from '../photo-utils';
import { formatAmount, formatUnit, scaleQuantity } from '../scaling';
import { ServingsStepper } from '../servings-stepper';

@Component({
  selector: 'app-recipe-detail',
  imports: [RouterLink, StarRating, ServingsStepper],
  template: `
    <a routerLink="/recipes" class="back">← All recipes</a>
    @if (recipe(); as r) {
      <div class="page-header">
  <h1>{{ r.title }}</h1>
  <a [routerLink]="['/recipes', r.id, 'edit']" class="button">Edit</a>
</div>
      <p class="meta">
        {{ r.cuisine ?? 'Any cuisine' }} · Prep {{ r.prepMinutes }} min · Cook {{ r.cookMinutes }} min · Serves {{ r.servings }}
      </p>
      @if (r.description) {
        <p>{{ r.description }}</p>
      }

      @if (photoVersion(); as v) {
        <img class="recipe-photo" [src]="'/api/recipes/' + r.id + '/photo?v=' + v" [alt]="'Photo of ' + r.title" />
      }
      <div class="row photo-actions">
        <label class="button" [class.disabled]="uploading()">
          {{ uploading() ? 'Uploading…' : (photoVersion() ? 'Change photo' : '📷 Add a photo') }}
          <input type="file" accept="image/*" hidden (change)="uploadPhoto(r.id, $event)" [disabled]="uploading()" />
        </label>
        @if (photoVersion()) {
          <button type="button" class="link" (click)="removePhoto(r.id)" [disabled]="uploading()">Remove photo</button>
        }
      </div>
      @if (photoError()) {
        <p class="error">{{ photoError() }}</p>
      }


      @if (social(); as s) {
        <section class="card social">
          <div class="rating-row">
            <span><strong>Your rating</strong> <span class="muted small-text note">(only you see this)</span></span>
            <app-star-rating [value]="s.myRating" [disabled]="busy()" label="Your rating" (rate)="rate(r.id, $event)" />
            @if (s.myRating) {
              <button type="button" class="link" (click)="clearRating(r.id)" [disabled]="busy()">Clear</button>
            }
          </div>
          @if (s.shareToken) {
            <div class="rating-row">
              <strong>Community</strong>
              @if (s.communityCount) {
                <app-star-rating [value]="s.communityAverage" [readonly]="true" label="Community rating" />
                <span class="muted">{{ s.communityAverage }} from {{ s.communityCount }} {{ s.communityCount === 1 ? 'rating' : 'ratings' }}</span>
              } @else {
                <span class="muted">No ratings yet</span>
              }
            </div>
          }

          <div class="share">
            @if (s.shareToken) {
              <p class="small-text"><strong>Shared.</strong> Anyone with this link can view the recipe. Logged-in users can rate it.</p>
              <div class="row">
                <input class="grow" readonly [value]="link()" aria-label="Share link" (focus)="$any($event.target).select()" />
                <button type="button" (click)="copy()">{{ copied() ? 'Copied ✓' : 'Copy link' }}</button>
                @if (canNativeShare) {
                  <button type="button" (click)="nativeShare(r.title)">Share…</button>
                }
              </div>
              <div class="share-targets">
                @for (target of targets(); track target.name) {
                  <a class="button" [href]="target.url" target="_blank" rel="noopener noreferrer">{{ target.name }}</a>
                }
              </div>
              <button type="button" class="link" (click)="unshare(r.id)" [disabled]="busy()">Stop sharing</button>
            } @else {
              <p class="small-text muted">This recipe is private. Sharing creates a link you can post anywhere; you can turn it off at any time.</p>
              <button type="button" class="primary" (click)="share(r.id)" [disabled]="busy()">Share this recipe</button>
            }
          </div>
          @if (error()) {
            <p class="error">{{ error() }}</p>
          }
        </section>
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
        <app-servings-stepper [value]="servings() ?? r.servings" [base]="r.servings" (valueChange)="servings.set($event)" />
        <div class="row add-to-list">
          <button type="button" (click)="addToList(r.id, r.servings)" [disabled]="adding()">
            {{ adding() ? 'Adding…' : '🛒 Add to shopping list' }}
          </button>
          @if (listNote()) {
            <span class="muted small-text">{{ listNote() }} <a routerLink="/shopping-list">View list</a></span>
          }
        </div>
        <ul>
          @for (i of r.ingredients; track i.ingredientId) {
            <li>
              {{ amount(scale(i.quantity, r.servings), i.unit) }} {{ unit(i.unit) }} {{ i.name }}
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

  // A local copy of the sharing/rating info that updates instantly when you click
  social = linkedSignal(() => this.recipe()?.social ?? null);
  busy = signal(false);
  photoVersion = linkedSignal(() => this.recipe()?.photoVersion ?? null);
  uploading = signal(false);
  photoError = signal<string | null>(null);
  // null = the recipe's own servings. Resets when you open a different recipe.
  servings = linkedSignal<unknown, number | null>({ source: this.recipe, computation: () => null });
  amount = formatAmount;
  unit = formatUnit;

  scale(quantity: number, base: number) {
    return scaleQuantity(quantity, base, this.servings() ?? base);
  }
  adding = signal(false);
  listNote = signal<string | null>(null);
  error = signal<string | null>(null);
  copied = signal(false);
  readonly canNativeShare = typeof navigator !== 'undefined' && 'share' in navigator;

  // The /s/ link shows a preview card on social media, then forwards to the recipe page
  link = computed(() => {
    const token = this.social()?.shareToken;
    return token ? `${location.origin}/s/${token}` : '';
  });
  targets = computed(() => shareTargets(this.link(), this.recipe()?.title ?? 'Recipe'));

  async addToList(id: number, recipeServings: number) {
    this.adding.set(true);
    this.listNote.set(null);
    try {
      const servings = this.servings() ?? recipeServings;
      await firstValueFrom(this.api.addRecipeToGroceryList(id, servings));
      this.listNote.set(`Added ingredients for ${servings} serving${servings === 1 ? '' : 's'}.`);
    } catch {
      this.listNote.set("Couldn't add to your list.");
    } finally {
      this.adding.set(false);
    }
  }

  async uploadPhoto(id: number, event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.uploading.set(true);
    this.photoError.set(null);
    try {
      // Shrink in the browser first: much faster on mobile data, and the GPS location is dropped
      // before the photo ever leaves the phone. The server still re-checks and re-encodes it.
      const photo = await shrinkPhoto(file, 1600, 0.88);
      const { photoVersion } = await firstValueFrom(this.api.uploadRecipePhoto(id, photo));
      this.photoVersion.set(photoVersion);
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.photoError.set(
        e instanceof PhotoReadError || status === 400 || status === 415 ? "That file couldn't be read as a photo." :
        status === 413 ? 'That photo is too large.' :
        status === 429 ? "You've uploaded a lot of photos this hour. Try again later." :
        status === 503 ? 'Photo uploads are unavailable right now.' :
        "Couldn't upload the photo. Please try again.");
    } finally {
      this.uploading.set(false);
    }
  }

  async removePhoto(id: number) {
    if (!confirm('Remove this photo?')) return;
    this.uploading.set(true);
    this.photoError.set(null);
    try {
      await firstValueFrom(this.api.removeRecipePhoto(id));
      this.photoVersion.set(null);
    } catch {
      this.photoError.set("Couldn't remove the photo. Please try again.");
    } finally {
      this.uploading.set(false);
    }
  }

  async rate(id: number, stars: number) {
    await this.run(async () => {
      await firstValueFrom(this.api.rateMyRecipe(id, stars));
      this.social.update(s => s && { ...s, myRating: stars });
    });
  }

  async clearRating(id: number) {
    await this.run(async () => {
      await firstValueFrom(this.api.clearMyRecipeRating(id));
      this.social.update(s => s && { ...s, myRating: null });
    });
  }

  async share(id: number) {
    await this.run(async () => {
      const { shareToken } = await firstValueFrom(this.api.shareRecipe(id));
      this.social.update(s => s && { ...s, shareToken });
    });
  }

  async unshare(id: number) {
    if (!confirm('Stop sharing? The current link will stop working for everyone. Sharing again later creates a new link.')) return;
    await this.run(async () => {
      await firstValueFrom(this.api.unshareRecipe(id));
      this.social.update(s => s && { ...s, shareToken: null });
    });
  }

  async copy() {
    try {
      await navigator.clipboard.writeText(this.link());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.error.set('Copying failed. Select the link and copy it manually.');
    }
  }

  // On phones this opens the system share sheet: Messages, Instagram, WhatsApp, and so on
  async nativeShare(title: string) {
    try {
      await navigator.share({ title, text: `${title}: a recipe I want to share`, url: this.link() });
    } catch {
      // The user closed the share sheet; nothing to do
    }
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch {
      this.error.set('Something went wrong. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}

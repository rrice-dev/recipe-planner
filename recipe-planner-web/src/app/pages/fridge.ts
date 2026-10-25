import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { FridgeMealIdea, FridgeResponse, RecipeDraft } from '../models';
import { PhotoReadError, shrinkPhoto } from '../photo-utils';

// Keep in sync with FridgePlanner on the API (the API enforces these; this is just friendlier)
const MAX_INGREDIENTS = 25;
const MAX_LENGTH = 40;
const ALLOWED = /^[\p{L}\p{M}\p{N} '\-.,&()/]+$/u;

@Component({
  selector: 'app-fridge',
  imports: [FormsModule, RouterLink],
  template: `
    <h1>What can I make?</h1>
    <p class="muted">List what's in your fridge and pantry. You'll see which of your recipes fit, plus a few new ideas.</p>

    <form class="card create" (ngSubmit)="add()">
      <div class="row">
        <input class="grow" name="ingredient" [(ngModel)]="draft" placeholder="e.g. chicken thighs, then press Enter"
               [attr.maxlength]="maxLength" autocomplete="off" />
        <button type="submit">Add</button>
        <label class="button" [class.disabled]="scanning()">
          {{ scanning() ? 'Scanning…' : '📷 Scan a photo' }}
          <input type="file" accept="image/*" hidden (change)="scan($event)" [disabled]="scanning()" />
        </label>
      </div>
      @if (scanNote()) {
        <p class="muted small-text">{{ scanNote() }}</p>
      }
      @if (inputError()) {
        <p class="error">{{ inputError() }}</p>
      }
      @if (ingredients().length) {
        <div class="chips">
          @for (item of ingredients(); track item) {
            <span class="chip">{{ item }}
              <button type="button" class="link" (click)="remove(item)" [attr.aria-label]="'Remove ' + item">×</button>
            </span>
          }
        </div>
      }
      <div class="actions">
        <button type="button" class="primary" (click)="suggest()" [disabled]="busy() || !ingredients().length">
          {{ busy() ? 'Thinking…' : 'Find meals' }}
        </button>
      </div>
    </form>

    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    @if (result(); as r) {
      <h2>From your recipes</h2>
      @if (r.fromYourRecipes.length) {
        <div class="grid">
          @for (m of r.fromYourRecipes; track m.recipeId) {
            <a class="card" [routerLink]="['/recipes', m.recipeId]">
              <h2>{{ m.title }}</h2>
              <p class="small-text"><strong>You have:</strong> {{ m.have.join(', ') }}</p>
              @if (m.missing.length) {
                <p class="small-text muted"><strong>Still need:</strong> {{ m.missing.join(', ') }}</p>
              } @else {
                <p class="small-text"><span class="chip small">Ready to cook</span></p>
              }
            </a>
          }
        </div>
      } @else {
        <p class="muted">None of your recipes use these ingredients yet.</p>
      }

      <h2>New ideas</h2>
      @if (r.aiNotice) {
        <p class="muted">{{ r.aiNotice }}</p>
      }
      @if (r.newIdeas.length) {
        <div class="grid">
          @for (idea of r.newIdeas; track idea.name) {
            <article class="card">
              <h2>{{ idea.name }}</h2>
              <p>{{ idea.description }}</p>
              @if (idea.usesIngredients.length) {
                <p class="small-text"><strong>Uses:</strong> {{ idea.usesIngredients.join(', ') }}</p>
              }
              @if (idea.extraIngredients.length) {
                <p class="small-text muted"><strong>You'd also need:</strong> {{ idea.extraIngredients.join(', ') }}</p>
              }
              @if (idea.techniques.length) {
                <div class="chips">
                  @for (t of idea.techniques; track t.id) {
                    <a class="chip" routerLink="/techniques">{{ t.name }}</a>
                  }
                </div>
              }
              <div class="actions small-actions">
                <button type="button" (click)="openRecipe(idea)" [disabled]="loadingRecipe() === idea.name">
                  {{ loadingRecipe() === idea.name ? 'Writing recipe…' : (openIdea() === idea.name ? 'Showing below ↓' : 'See full recipe') }}
                </button>
              </div>
            </article>
          }
        </div>

        @if (recipeError()) {
          <p class="error">{{ recipeError() }}</p>
        }

        @if (openRecipeDraft(); as d) {
          <article class="card recipe-draft" id="recipe-draft">
            <div class="page-header">
              <h2>{{ d.title }}</h2>
              <button type="button" class="link" (click)="closeRecipe()" aria-label="Close recipe">Close ×</button>
            </div>
            @if (d.description) {
              <p>{{ d.description }}</p>
            }
            <p class="meta">Serves {{ d.servings }} · Prep {{ d.prepMinutes }} min · Cook {{ d.cookMinutes }} min</p>

            <h3>Ingredients</h3>
            <ul class="draft-ingredients">
              @for (i of d.ingredients; track i.name) {
                <li [class.need]="!i.onHand">
                  <span class="have-mark" [attr.aria-label]="i.onHand ? 'You have this' : 'You need this'">{{ i.onHand ? '✓' : '+' }}</span>
                  <span>{{ i.quantity }} {{ i.unit }} {{ i.name }}@if (i.note) {<span class="muted">, {{ i.note }}</span>}</span>
                </li>
              }
            </ul>
            <p class="muted small-text">✓ you have it · + you'd need to get it</p>

            <h3>Steps</h3>
            <ol class="draft-steps">
              @for (step of d.steps; track $index) {
                <li>
                  {{ step.instruction }}
                  @if (step.techniqueName) {
                    <a class="chip small" routerLink="/techniques">{{ step.techniqueName }}</a>
                  }
                </li>
              }
            </ol>

            <div class="actions">
              <button type="button" class="primary" (click)="saveRecipe(d)" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save to my recipes' }}
              </button>
            </div>
            @if (saveError()) {
              <p class="error">{{ saveError() }}</p>
            }
            <p class="muted small-text">This recipe is AI-generated. Check cooking times and use a thermometer for meat, poultry and eggs.</p>
          </article>
        }
        <p class="muted small-text">Ideas are AI-generated. Use your judgment on food safety and cooking times.</p>
      } @else if (r.aiAvailable && !r.aiNotice) {
        <p class="muted">No new ideas this time. Try adding a few more ingredients.</p>
      }
    }
  `
})
export class FridgePage {
  private api = inject(ApiService);

  readonly maxLength = MAX_LENGTH;
  draft = '';
  ingredients = signal<string[]>([]);
  inputError = signal<string | null>(null);
  error = signal<string | null>(null);
  busy = signal(false);
  scanning = signal(false);
  loadingRecipe = signal<string | null>(null);
  openIdea = signal<string | null>(null);
  recipeError = signal<string | null>(null);
  saving = signal(false);
  saveError = signal<string | null>(null);
  private recipeCache = new Map<string, RecipeDraft>(); // opening the same idea twice doesn't cost a second AI call
  private router = inject(Router);

  openRecipeDraft() {
    const name = this.openIdea();
    return name ? this.recipeCache.get(name) ?? null : null;
  }
  scanNote = signal<string | null>(null);
  result = signal<FridgeResponse | null>(null);

  add() {
    this.inputError.set(null);
    // Allow pasting a comma-separated list
    const items = this.draft.split(',').map(s => s.trim()).filter(s => s.length);
    if (!items.length) return;

    const current = this.ingredients();
    for (const item of items) {
      if (item.length > MAX_LENGTH) return this.inputError.set(`Keep each ingredient under ${MAX_LENGTH} characters.`);
      if (!ALLOWED.test(item)) return this.inputError.set('Use letters, numbers and basic punctuation only.');
    }
    const merged = [...current];
    for (const item of items) {
      if (!merged.some(m => m.toLowerCase() === item.toLowerCase())) merged.push(item);
    }
    if (merged.length > MAX_INGREDIENTS) return this.inputError.set(`You can add up to ${MAX_INGREDIENTS} ingredients.`);

    this.ingredients.set(merged);
    this.draft = '';
  }

  async openRecipe(idea: FridgeMealIdea) {
    this.recipeError.set(null);
    this.saveError.set(null);
    if (!this.recipeCache.has(idea.name)) {
      this.loadingRecipe.set(idea.name);
      try {
        const draft = await firstValueFrom(this.api.getFridgeRecipe(idea.name, this.ingredients()));
        this.recipeCache.set(idea.name, draft);
      } catch (e) {
        const status = e instanceof HttpErrorResponse ? e.status : 0;
        this.recipeError.set(
          status === 429 ? "You've opened a lot of recipes this hour. Try again later." :
          status === 400 ? "That dish name couldn't be used. Try another idea." :
          "Couldn't write that recipe right now. Please try again.");
        return;
      } finally {
        this.loadingRecipe.set(null);
      }
    }
    this.openIdea.set(idea.name);
    setTimeout(() => document.getElementById('recipe-draft')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  closeRecipe() {
    this.openIdea.set(null);
  }

  // Saves through the same endpoints the recipe editor uses, so all their validation applies
  async saveRecipe(draft: RecipeDraft) {
    this.saving.set(true);
    this.saveError.set(null);
    try {
      // 1. Find or create each ingredient in the shared list
      const known = new Map((await firstValueFrom(this.api.getIngredients())).map(i => [i.name.toLowerCase(), i.id]));
      const items: { ingredientId: number; quantity: number; unit: string; note: string | null }[] = [];
      for (const i of draft.ingredients) {
        let id = known.get(i.name.toLowerCase());
        if (id === undefined) {
          try {
            id = (await firstValueFrom(this.api.createIngredient(i.name, null))).id;
          } catch (e) {
            // Someone else created it a moment ago: look it up instead
            if (!(e instanceof HttpErrorResponse && e.status === 409)) throw e;
            const fresh = await firstValueFrom(this.api.getIngredients());
            id = fresh.find(f => f.name.toLowerCase() === i.name.toLowerCase())?.id;
            if (id === undefined) throw e;
          }
          known.set(i.name.toLowerCase(), id);
        }
        if (!items.some(x => x.ingredientId === id)) {
          items.push({ ingredientId: id, quantity: i.quantity, unit: i.unit, note: i.note });
        }
      }

      // 2. The recipe itself, then its ingredients, steps and techniques
      const recipe = await firstValueFrom(this.api.createRecipe({
        title: draft.title,
        description: draft.description || null,
        cuisine: null,
        servings: draft.servings,
        prepMinutes: draft.prepMinutes,
        cookMinutes: draft.cookMinutes
      }));
      await firstValueFrom(this.api.setRecipeIngredients(recipe.id, items));
      await firstValueFrom(this.api.setRecipeSteps(recipe.id,
        draft.steps.map(s => ({ instruction: s.instruction, techniqueId: s.techniqueId }))));
      const techniqueIds = [...new Set(draft.steps.map(s => s.techniqueId).filter((id): id is number => id !== null))];
      for (const id of techniqueIds) {
        await firstValueFrom(this.api.linkTechnique(recipe.id, id));
      }

      await this.router.navigate(['/recipes', recipe.id]);
    } catch {
      this.saveError.set("Couldn't save the recipe. Please try again.");
    } finally {
      this.saving.set(false);
    }
  }

  async scan(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // so picking the same photo again still triggers a scan
    if (!file) return;

    this.inputError.set(null);
    this.scanNote.set(null);
    this.scanning.set(true);
    try {
      const photo = await shrinkPhoto(file);
      const { ingredients } = await firstValueFrom(this.api.scanPhoto(photo));

      const merged = [...this.ingredients()];
      let added = 0;
      for (const item of ingredients) {
        if (merged.length >= MAX_INGREDIENTS) break;
        if (!merged.some(m => m.toLowerCase() === item.toLowerCase())) {
          merged.push(item);
          added++;
        }
      }
      this.ingredients.set(merged);
      // The AI's list is a draft: the user reviews it before anything else happens
      this.scanNote.set(added
        ? `Found ${added} item${added === 1 ? '' : 's'}. Remove anything that's wrong and add what it missed.`
        : "Couldn't spot any new food in that photo. Try a closer, well-lit shot.");
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.inputError.set(
        status === 429 ? "You've used your photo scans for this hour. Try again later." :
        status === 413 ? 'That photo is too large.' :
        status === 400 || status === 415 ? "That file couldn't be read as a photo." :
        status === 503 ? 'Photo scanning is unavailable right now.' :
        e instanceof PhotoReadError ? "That file couldn't be read as a photo." :
        'Something went wrong scanning the photo. Please try again.');
    } finally {
      this.scanning.set(false);
    }
  }

  remove(item: string) {
    this.ingredients.update(list => list.filter(i => i !== item));
  }

  async suggest() {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.result.set(await firstValueFrom(this.api.getFridgeSuggestions(this.ingredients())));
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.error.set(
        status === 429 ? "You've used your AI suggestions for this hour. Try again later." :
        status === 400 ? 'Check your ingredients and try again.' :
        'Something went wrong. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}

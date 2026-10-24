import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { FridgeResponse } from '../models';

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
      </div>
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
            </article>
          }
        </div>
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

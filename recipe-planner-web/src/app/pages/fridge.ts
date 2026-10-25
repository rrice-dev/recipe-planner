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
  scanning = signal(false);
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

class PhotoReadError extends Error {}

// Shrinks the photo in the browser before uploading: much faster on mobile data, and the
// server would shrink it anyway. Drawing to a canvas also drops the photo's EXIF data
// (including GPS location) before it ever leaves the phone. The server still re-checks everything.
async function shrinkPhoto(file: File, maxEdge = 1024): Promise<Blob> {
  let source: ImageBitmap;
  try {
    source = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    throw new PhotoReadError();
  }

  const scale = Math.min(1, maxEdge / Math.max(source.width, source.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(source.width * scale));
  canvas.height = Math.max(1, Math.round(source.height * scale));
  canvas.getContext('2d')!.drawImage(source, 0, 0, canvas.width, canvas.height);
  source.close();

  const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
  if (!blob) throw new PhotoReadError();
  return blob;
}

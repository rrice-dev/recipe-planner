import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { DraftSaver } from '../draft-saver';
import { RecipeDraft } from '../models';
import { SettingsService } from '../settings.service';
import { formatMeasure } from '../scaling';
import { totalTime } from '../library-utils';
import { MAX_PAGES, PhotoReadError, blobToDataUrl, shrinkPhoto } from '../photo-utils';
import { CONFIRM_EMAIL_MESSAGE, ConfirmEmailNotice, needsConfirmedEmail } from '../confirm-email';

type Mode = 'link' | 'photo' | 'paste';

interface Page { file: File; preview: string; }

// Bring in a recipe you already have: a link to any recipe page, photos of a cookbook page or
// a handwritten card, or text pasted from anywhere. You always see it before anything is saved.
@Component({
  selector: 'app-import-recipe',
  imports: [RouterLink, ConfirmEmailNotice],
  template: `
    <a routerLink="/recipes" class="back">← Recipes</a>
    <h1>Import a recipe</h1>
    <app-confirm-email-notice />

    <div class="row filter-chips" role="group" aria-label="Import from">
      @for (m of modes; track m.value) {
        <button type="button" class="chip-button" [class.selected]="mode() === m.value" [attr.aria-pressed]="mode() === m.value"
                (click)="setMode(m.value)" [disabled]="busy()">{{ m.label }}</button>
      }
    </div>

    @switch (mode()) {
      @case ('link') {
        <p class="muted">Paste a link to a recipe on any website. You'll see it here before anything is saved.</p>
        <form class="row import-form" (submit)="$event.preventDefault(); fetch()">
          <input class="grow" type="url" inputmode="url" placeholder="https://…" aria-label="Recipe link"
                 [value]="url()" (input)="url.set($any($event.target).value)" required />
          <button type="submit" class="primary" [disabled]="busy() || !url().trim()">{{ busy() ? 'Reading the page…' : 'Import' }}</button>
        </form>
      }
      @case ('photo') {
        <p class="muted">Snap a cookbook page or a handwritten recipe card. Front and back, or two pages? Add them all, in order (up to {{ maxPages }}).</p>
        @if (pages().length) {
          <ol class="page-thumbs">
            @for (p of pages(); track p.preview; let i = $index) {
              <li>
                <img [src]="p.preview" [alt]="'Page ' + (i + 1)" />
                <button type="button" class="link small-text" (click)="removePage(i)" [disabled]="busy()" [attr.aria-label]="'Remove page ' + (i + 1)">Remove</button>
              </li>
            }
          </ol>
        }
        <div class="row">
          @if (pages().length < maxPages) {
            <label class="button" [class.disabled]="busy()">
              📷 {{ pages().length ? 'Add another page' : 'Take a photo' }}
              <input type="file" accept="image/*" capture="environment" hidden (change)="addPages($event)" [disabled]="busy()" />
            </label>
            <label class="button" [class.disabled]="busy()">
              Choose from photos
              <input type="file" accept="image/*" multiple hidden (change)="addPages($event)" [disabled]="busy()" />
            </label>
          }
          @if (pages().length) {
            <button type="button" class="primary" (click)="readPhotos()" [disabled]="busy()">{{ busy() ? 'Reading the recipe…' : 'Read recipe' }}</button>
          }
        </div>
        @if (busy()) { <p class="small-text muted">Handwriting can take up to a minute to read.</p> }
        <p class="small-text muted">Tip: fill the frame with the recipe, in good light, without glare.</p>
      }
      @case ('paste') {
        <p class="muted">Paste a recipe from an email, a document or a website. Extra text around it is fine.</p>
        <form class="stack" (submit)="$event.preventDefault(); readText()">
          <textarea rows="10" aria-label="Recipe text" placeholder="Paste the ingredients and steps here…"
                    [value]="text()" (input)="text.set($any($event.target).value)" maxlength="20000"></textarea>
          <div class="row">
            <button type="submit" class="primary" [disabled]="busy() || text().trim().length < 20">{{ busy() ? 'Reading…' : 'Read recipe' }}</button>
          </div>
        </form>
      }
    }
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

    @if (draft(); as r) {
      <article class="card recipe-draft">
        <p class="small-text muted">
          @switch (from()) {
            @case ('link') { From <a [href]="source()" target="_blank" rel="noopener noreferrer nofollow">{{ host() }}</a> }
            @case ('photo') { From your {{ readPages().length === 1 ? 'photo' : readPages().length + ' photos' }} }
            @case ('paste') { From pasted text }
          }
        </p>
        <h2>{{ r.title }}</h2>
        @if (r.description) { <p>{{ r.description }}</p> }
        <dl class="facts">
          <div><dt>Prep</dt><dd>{{ time(r.prepMinutes) }}</dd></div>
          <div><dt>Cook</dt><dd>{{ time(r.cookMinutes) }}</dd></div>
          <div><dt>Serves</dt><dd>{{ r.servings }}</dd></div>
        </dl>
        @if (unclear().length) {
          <div class="callout small-text" role="note">
            <strong>Check these before you cook:</strong>
            <ul class="unclear">
              @for (u of unclear(); track u) { <li>{{ u }}</li> }
            </ul>
            You can fix anything in the editor after saving.
          </div>
        } @else if (from() !== 'link' || method() === 'ai') {
          <p class="callout small-text">AI typed this up. Double-check the amounts before you cook.</p>
        }
        <h3>Ingredients</h3>
        <ul>
          @for (i of r.ingredients; track i.name) {
            <li>{{ measure(i.quantity, i.unit) }} {{ i.name }}@if (i.note) {<span class="muted">, {{ i.note }}</span>}</li>
          }
        </ul>
        <h3>Steps</h3>
        <ol class="draft-steps">
          @for (s of r.steps; track $index) {
            <li>{{ s.instruction }} @if (s.techniqueName) { <span class="chip small">{{ s.techniqueName }}</span> }</li>
          }
        </ol>

        @if (from() === 'photo') {
          <label class="check">
            <input type="checkbox" [checked]="keepPages()" (change)="keepPages.set($any($event.target).checked)" />
            Keep the original {{ readPages().length === 1 ? 'photo' : 'photos' }} with the recipe (only you can see them)
          </label>
        }
        @if (from() !== 'link') {
          <label class="check">
            <input type="checkbox" [checked]="fromCookbook()" (change)="fromCookbook.set($any($event.target).checked)" />
            Copied from a published cookbook or magazine (keeps it private: it can't be shared)
          </label>
        }

        <div class="actions">
          <button type="button" class="primary" (click)="save(r)" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save to my recipes' }}</button>
          <button type="button" (click)="discard()" [disabled]="saving()">Discard</button>
        </div>
        @if (saveError(); as e) {
          <p class="error" role="alert">{{ e }}
            @if (savedId(); as id) { <a [routerLink]="['/recipes', id]">Open the recipe</a> }
          </p>
        }
        @if (from() === 'link') {
          <p class="small-text muted">Saved recipes keep a link back to the original site. You can edit everything after saving.</p>
        } @else {
          <p class="small-text muted">You can edit everything after saving.</p>
        }
      </article>
    }
  `,
  styles: `
    .page-thumbs { list-style: none; padding: 0; margin: 0 0 0.75rem; display: flex; flex-wrap: wrap; gap: 0.75rem; counter-reset: page; }
    .page-thumbs li { display: flex; flex-direction: column; align-items: center; gap: 0.25rem; counter-increment: page; position: relative; }
    .page-thumbs li::before { content: counter(page); position: absolute; top: 0.3rem; left: 0.3rem; background: var(--surface);
      border-radius: 999px; min-width: 1.4rem; text-align: center; font-size: 0.8rem; font-weight: 700; padding: 0 0.3rem; }
    .page-thumbs img { width: 110px; height: 140px; object-fit: cover; border-radius: 10px; border: 1px solid var(--border); background: var(--surface-2); }
    textarea { width: 100%; font: inherit; }
    .stack { display: flex; flex-direction: column; gap: 0.5rem; }
    .unclear { margin: 0.35rem 0; padding-left: 1.2rem; }
    .check { display: flex; gap: 0.5rem; align-items: flex-start; margin: 0.5rem 0; }
    .check input { margin-top: 0.25rem; }
  `
})
export class ImportRecipePage {
  private api = inject(ApiService);
  private router = inject(Router);
  private saver = inject(DraftSaver);
  private units = inject(SettingsService).settings;
  time = totalTime;
  readonly maxPages = MAX_PAGES;
  readonly modes: { value: Mode; label: string }[] = [
    { value: 'link', label: '🔗 Link' },
    { value: 'photo', label: '📷 Photo' },
    { value: 'paste', label: '📋 Paste text' }
  ];

  mode = signal<Mode>('link');
  url = signal('');
  text = signal('');
  pages = signal<Page[]>([]);
  busy = signal(false);
  error = signal<string | null>(null);

  // The recipe being reviewed, and where it came from
  draft = signal<RecipeDraft | null>(null);
  from = signal<Mode>('link');
  source = signal('');
  method = signal<'structured' | 'ai'>('structured');
  unclear = signal<string[]>([]);
  readPages = signal<Page[]>([]);   // the photos this draft was read from
  keepPages = signal(true);
  fromCookbook = signal(false);

  saving = signal(false);
  saveError = signal<string | null>(null);
  savedId = signal<number | null>(null);

  measure = (q: number, unit: string) => formatMeasure(q, unit, this.units().units);

  constructor() {
    // Previews are object URLs: release them when leaving the page
    inject(DestroyRef).onDestroy(() => this.allPages().forEach(p => URL.revokeObjectURL(p.preview)));

    const q = inject(ActivatedRoute).snapshot.queryParamMap;
    const mode = q.get('mode');
    if (mode === 'photo' || mode === 'paste') this.mode.set(mode);

    // Shared from another app (Android): a link arrives as ?url= or inside ?text=; plain text goes to Paste
    const sharedText = q.get('text') ?? '';
    const shared = q.get('url') || sharedText.match(/https?:\/\/\S+/)?.[0] || '';
    if (shared) {
      this.url.set(shared);
      void this.fetch();
    } else if (sharedText.trim().length >= 20) {
      this.mode.set('paste');
      this.text.set(sharedText);
    }
  }

  setMode(mode: Mode) {
    this.mode.set(mode);
    this.error.set(null);
  }

  host() {
    try { return new URL(this.source()).hostname.replace(/^www\./, ''); } catch { return 'the original site'; }
  }

  // ---------- Link ----------

  async fetch() {
    const url = this.url().trim();
    if (!url) return;
    await this.run('link', async () => {
      const result = await firstValueFrom(this.api.importRecipe(url.match(/^https?:\/\//i) ? url : `https://${url}`));
      this.show(result.recipe, 'link', []);
      this.source.set(result.sourceUrl);
      this.method.set(result.method);
    });
  }

  // ---------- Photos ----------

  addPages(event: Event) {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    const room = MAX_PAGES - this.pages().length;
    if (files.length > room) this.error.set(`Only ${MAX_PAGES} pages fit in one recipe, so the first ${room} were added.`);
    else this.error.set(null);
    const added = files.slice(0, room).map(file => ({ file, preview: URL.createObjectURL(file) }));
    this.pages.update(p => [...p, ...added]);
  }

  removePage(index: number) {
    const page = this.pages()[index];
    this.pages.update(p => p.filter((_, i) => i !== index));
    if (!this.readPages().includes(page)) URL.revokeObjectURL(page.preview);
  }

  async readPhotos() {
    const pages = this.pages();
    if (!pages.length) return;
    await this.run('photo', async () => {
      // Shrink in the browser first: the upload is much smaller and EXIF/GPS never leaves the phone
      const photos = await Promise.all(pages.map(p => shrinkPhoto(p.file, 1568, 0.85).then(blobToDataUrl)));
      const result = await firstValueFrom(this.api.readRecipePhotos(photos));
      this.show(result.recipe, 'photo', result.unclear);
      this.readPages.set(pages);
      this.keepPages.set(true);
    });
  }

  // ---------- Paste ----------

  async readText() {
    const text = this.text().trim();
    if (text.length < 20) return;
    await this.run('paste', async () => {
      const result = await firstValueFrom(this.api.readRecipeText(text));
      this.show(result.recipe, 'paste', result.unclear);
    });
  }

  // ---------- Review and save ----------

  discard() {
    this.draft.set(null);
    this.saveError.set(null);
    this.savedId.set(null);
  }

  async save(draft: RecipeDraft) {
    this.saving.set(true);
    this.saveError.set(null);
    let id = this.savedId();
    try {
      if (id === null) {
        const from = this.from();
        id = await this.saver.save(draft, null, from === 'link' ? this.source() : null, from !== 'link' && this.fromCookbook());
        this.savedId.set(id);
      }
    } catch {
      this.saveError.set("Couldn't save the recipe. Please try again.");
      this.saving.set(false);
      return;
    }

    if (this.from() === 'photo' && this.keepPages()) {
      try {
        for (const page of this.readPages()) {
          await firstValueFrom(this.api.addSourcePage(id, await shrinkPhoto(page.file, 2000, 0.88)));
        }
      } catch {
        // The recipe itself is saved; pages can be added again from the recipe page
        this.saveError.set("The recipe was saved, but the original photos couldn't be kept. You can add them from the recipe page.");
        this.saving.set(false);
        return;
      }
    }

    this.saving.set(false);
    await this.router.navigate(['/recipes', id]);
  }

  private show(recipe: RecipeDraft, from: Mode, unclear: string[]) {
    this.draft.set(recipe);
    this.from.set(from);
    this.unclear.set(unclear);
    this.method.set('ai');
    this.fromCookbook.set(false);
    this.savedId.set(null);
    this.saveError.set(null);
  }

  private allPages() {
    return [...new Set([...this.pages(), ...this.readPages()])];
  }

  private async run(kind: Mode, action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    this.draft.set(null);
    try {
      await action();
    } catch (e) {
      const err = e instanceof HttpErrorResponse ? e : null;
      this.error.set(
        needsConfirmedEmail(e) ? CONFIRM_EMAIL_MESSAGE :
        e instanceof PhotoReadError ? "One of those files couldn't be opened as a photo." :
        err?.status === 429 ? (kind === 'link' ? "You've imported a lot of recipes this hour. Try again later."
                                                : "You've read a lot of recipes this hour. Try again later.") :
        err?.status === 413 ? 'Those photos are too large. Try fewer pages.' :
        err?.status === 503 ? (typeof err.error?.detail === 'string' ? err.error.detail : 'Reading recipes is unavailable right now.') :
        err?.status === 422 && typeof err.error?.detail === 'string' ? err.error.detail :
        kind === 'link' ? "Couldn't import that page. Please try again." : "Couldn't read the recipe. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

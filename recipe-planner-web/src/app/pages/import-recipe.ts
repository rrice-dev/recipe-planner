import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { DraftSaver } from '../draft-saver';
import { RecipeDraft } from '../models';
import { SettingsService } from '../settings.service';
import { formatMeasure } from '../scaling';
import { totalTime } from '../library-utils';

// Paste a link to any recipe page (or share it to the installed app on Android), check it, save it.
@Component({
  selector: 'app-import-recipe',
  imports: [RouterLink],
  template: `
    <a routerLink="/recipes" class="back">← Recipes</a>
    <h1>Import a recipe</h1>
    <p class="muted">Paste a link to a recipe on any website. You'll see it here before anything is saved.</p>

    <form class="row import-form" (submit)="$event.preventDefault(); fetch()">
      <input class="grow" type="url" inputmode="url" placeholder="https://…" aria-label="Recipe link"
             [value]="url()" (input)="url.set($any($event.target).value)" required />
      <button type="submit" class="primary" [disabled]="busy() || !url().trim()">{{ busy() ? 'Reading the page…' : 'Import' }}</button>
    </form>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

    @if (draft(); as r) {
      <article class="card recipe-draft">
        <p class="small-text muted">From <a [href]="source()" target="_blank" rel="noopener noreferrer nofollow">{{ host() }}</a></p>
        <h2>{{ r.title }}</h2>
        @if (r.description) { <p>{{ r.description }}</p> }
        <dl class="facts">
          <div><dt>Prep</dt><dd>{{ time(r.prepMinutes) }}</dd></div>
          <div><dt>Cook</dt><dd>{{ time(r.cookMinutes) }}</dd></div>
          <div><dt>Serves</dt><dd>{{ r.servings }}</dd></div>
        </dl>
        @if (method() === 'ai') {
          <p class="callout small-text">This page didn't include standard recipe data, so AI read the page instead. Double-check the amounts before you cook.</p>
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
        <div class="actions">
          <button type="button" class="primary" (click)="save(r)" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save to my recipes' }}</button>
          <button type="button" (click)="draft.set(null)">Discard</button>
        </div>
        @if (saveError()) { <p class="error" role="alert">{{ saveError() }}</p> }
        <p class="small-text muted">Saved recipes keep a link back to the original site. You can edit everything after saving.</p>
      </article>
    }
  `
})
export class ImportRecipePage {
  private api = inject(ApiService);
  private router = inject(Router);
  private saver = inject(DraftSaver);
  private units = inject(SettingsService).settings;
  time = totalTime;

  url = signal('');
  busy = signal(false);
  error = signal<string | null>(null);
  draft = signal<RecipeDraft | null>(null);
  source = signal('');
  method = signal<'structured' | 'ai'>('structured');
  saving = signal(false);
  saveError = signal<string | null>(null);

  measure = (q: number, unit: string) => formatMeasure(q, unit, this.units().units);

  constructor() {
    // Shared from another app (Android): the link arrives as ?url=, or inside ?text=
    const q = inject(ActivatedRoute).snapshot.queryParamMap;
    const shared = q.get('url') || (q.get('text') ?? '').match(/https?:\/\/\S+/)?.[0] || '';
    if (shared) {
      this.url.set(shared);
      void this.fetch();
    }
  }

  host() {
    try { return new URL(this.source()).hostname.replace(/^www\./, ''); } catch { return 'the original site'; }
  }

  async fetch() {
    const url = this.url().trim();
    if (!url) return;
    this.busy.set(true);
    this.error.set(null);
    this.draft.set(null);
    try {
      const result = await firstValueFrom(this.api.importRecipe(url.match(/^https?:\/\//i) ? url : `https://${url}`));
      this.draft.set(result.recipe);
      this.source.set(result.sourceUrl);
      this.method.set(result.method);
    } catch (e) {
      const err = e instanceof HttpErrorResponse ? e : null;
      this.error.set(err?.status === 429 ? "You've imported a lot of recipes this hour. Try again later."
        : err?.status === 422 && typeof err.error?.detail === 'string' ? err.error.detail
        : "Couldn't import that page. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }

  async save(draft: RecipeDraft) {
    this.saving.set(true);
    this.saveError.set(null);
    try {
      const id = await this.saver.save(draft, null, this.source());
      await this.router.navigate(['/recipes', id]);
    } catch {
      this.saveError.set("Couldn't save the recipe. Please try again.");
    } finally {
      this.saving.set(false);
    }
  }
}

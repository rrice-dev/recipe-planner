import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { Collection, RecipePersonal } from './models';
import { toIsoDate } from './date-utils';

// Favorite, "I cooked this", private notes and collections for one of your recipes.
// Only you ever see any of it, even when the recipe is shared.
@Component({
  selector: 'app-personal-panel',
  template: `
    <section class="card personal" style="margin-top: 0">
      <div class="row">
        <button type="button" (click)="toggleFavorite()" [attr.aria-pressed]="state().isFavorite">
          {{ state().isFavorite ? '★ Favorite' : '☆ Add to favorites' }}
        </button>
        <button type="button" (click)="markCooked()" [disabled]="busy()">I cooked this today</button>
        <details class="other-day">
          <summary class="small-text">Another day?</summary>
          <div class="row">
            <input type="date" [max]="today" [value]="otherDay()" (input)="otherDay.set($any($event.target).value)" aria-label="Day you cooked it" />
            <button type="button" (click)="markCooked(otherDay())" [disabled]="busy() || !otherDay()">Add</button>
          </div>
        </details>
      </div>

      @if (state().cooked.length) {
        <p class="small-text">Cooked {{ state().cooked.length === 1 ? 'once' : state().cooked.length + ' times' }}:</p>
        <ul class="history">
          @for (c of state().cooked; track c.id) {
            <li class="chip">{{ date(c.cookedOn) }}
              <button type="button" class="link" (click)="removeCooked(c.id)" [attr.aria-label]="'Remove ' + date(c.cookedOn)">×</button>
            </li>
          }
        </ul>
      } @else {
        <p class="small-text muted">You haven't marked this as cooked yet.</p>
      }

      <h3>My notes</h3>
      <textarea class="notes-box" maxlength="4000" aria-label="Your private notes for this recipe"
                placeholder="Changes you made, what to try next time…" [value]="notesDraft()" (input)="notesDraft.set($any($event.target).value)"></textarea>
      <div class="row">
        <button type="button" (click)="saveNotes()" [disabled]="busy() || notesDraft() === (state().notes ?? '')">Save notes</button>
        <span class="small-text muted">Only you can see these.</span>
      </div>

      <h3>Collections</h3>
      @if (collections().length) {
        <div class="collection-picks">
          @for (c of collections(); track c.id) {
            <label class="check-label"><input type="checkbox" [checked]="state().collectionIds.includes(c.id)" (change)="toggleCollection(c.id)" /> {{ c.name }}</label>
          }
        </div>
      }
      <div class="row">
        <input class="grow" placeholder="New collection, e.g. Weeknight" maxlength="60" aria-label="New collection name"
               [value]="newName()" (input)="newName.set($any($event.target).value)" (keydown.enter)="createCollection()" />
        <button type="button" (click)="createCollection()" [disabled]="busy() || !newName().trim()">Add collection</button>
      </div>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    </section>
  `
})
export class PersonalPanel {
  private api = inject(ApiService);

  recipeId = input.required<number>();
  personal = input.required<RecipePersonal>();

  state = linkedSignal(() => this.personal());
  notesDraft = linkedSignal(() => this.personal().notes ?? '');
  collections = signal<Collection[]>([]);
  newName = signal('');
  otherDay = signal('');
  busy = signal(false);
  error = signal<string | null>(null);
  readonly today = toIsoDate(new Date());

  constructor() {
    void firstValueFrom(this.api.getCollections()).then(c => this.collections.set(c)).catch(() => {});
  }

  date(iso: string) {
    const [y, m, d] = iso.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  async toggleFavorite() {
    const next = !this.state().isFavorite;
    await this.run(() => firstValueFrom(this.api.setFavorite(this.recipeId(), next)),
      () => this.state.update(s => ({ ...s, isFavorite: next })));
  }

  async markCooked(day: string | null = null) {
    // The server records today in UTC; send the local date so evening cooking counts for today
    const entry = await this.run(() => firstValueFrom(this.api.markCooked(this.recipeId(), day || this.today)));
    if (entry) {
      this.state.update(s => ({ ...s, cooked: [entry, ...s.cooked].sort((a, b) => b.cookedOn.localeCompare(a.cookedOn) || b.id - a.id) }));
      this.otherDay.set('');
    }
  }

  async removeCooked(entryId: number) {
    await this.run(() => firstValueFrom(this.api.removeCooked(this.recipeId(), entryId)),
      () => this.state.update(s => ({ ...s, cooked: s.cooked.filter(c => c.id !== entryId) })));
  }

  async saveNotes() {
    const notes = this.notesDraft().trim() || null;
    await this.run(() => firstValueFrom(this.api.setNotes(this.recipeId(), notes)),
      () => this.state.update(s => ({ ...s, notes })));
  }

  async toggleCollection(id: number) {
    const current = this.state().collectionIds;
    const next = current.includes(id) ? current.filter(x => x !== id) : [...current, id];
    await this.run(() => firstValueFrom(this.api.setRecipeCollections(this.recipeId(), next)),
      () => this.state.update(s => ({ ...s, collectionIds: next })));
  }

  async createCollection() {
    const name = this.newName().trim();
    if (!name) return;
    const created = await this.run(() => firstValueFrom(this.api.createCollection(name)));
    if (!created) return;
    this.collections.update(list => [...list, created].sort((a, b) => a.name.localeCompare(b.name)));
    this.newName.set('');
    await this.toggleCollection(created.id); // put this recipe in the new collection
  }

  private async run<T>(action: () => Promise<T>, onSuccess?: () => void): Promise<T | null> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await action();
      onSuccess?.();
      return result;
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 409 ? 'You already have a collection with that name.'
        : e instanceof HttpErrorResponse && e.status === 400 ? 'Check that and try again.'
        : "Couldn't save that. Please try again.");
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { Ingredient, Leftover, LeftoverLocation, PantryItem } from '../models';
import { toIsoDate } from '../date-utils';
import { COMMON_STAPLES, freshness, portionsLabel } from '../kitchen';

// Leftovers to eat (soonest first) and the pantry staples shopping lists skip
@Component({
  selector: 'app-kitchen',
  imports: [RouterLink],
  template: `
    <div class="page-header">
      <h1>Kitchen</h1>
    </div>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

    <section class="card kitchen-section">
      <h2>Leftovers</h2>
      <p class="small-text muted">Cooked food keeps 3 to 4 days in the fridge. Frozen food stays safe, and is best within 3 months.</p>

      @for (l of leftovers(); track l.id) {
        <div class="leftover" [attr.data-level]="fresh(l).level">
          <div class="grow">
            <strong>
              @if (l.recipeId) { <a [routerLink]="['/recipes', l.recipeId]" class="plain">{{ l.name }}</a> } @else { {{ l.name }} }
            </strong>
            <span class="chip small">{{ l.location === 'freezer' ? 'Freezer' : 'Fridge' }}</span>
            <div class="small-text">
              <span class="freshness">{{ fresh(l).label }}</span>
              <span class="muted"> · {{ portions(l.portions) }} · made {{ shortDate(l.madeOn) }}</span>
            </div>
          </div>
          <div class="row leftover-actions">
            @if (fresh(l).level !== 'past') {
              <button type="button" (click)="eat(l)" [disabled]="busy()">Ate one</button>
              @if (l.location === 'fridge') {
                <button type="button" (click)="move(l, 'freezer')" [disabled]="busy()">Freeze</button>
              } @else {
                <button type="button" (click)="move(l, 'fridge')" [disabled]="busy()" title="Thaw in the fridge, then eat within 3 to 4 days">Thaw</button>
              }
            }
            <button type="button" class="link" (click)="remove(l)" [disabled]="busy()">{{ fresh(l).level === 'past' ? 'Thrown out' : 'Remove' }}</button>
          </div>
        </div>
      } @empty {
        @if (loaded()) {
          <p class="muted">Nothing tracked yet. Save leftovers from a recipe, from cook mode, or from prep day, or add them here.</p>
        }
      }

      <details class="add-leftover" [open]="!leftovers().length && loaded()">
        <summary>Add leftovers</summary>
        <div class="row">
          <input class="grow" maxlength="100" placeholder="What is it? e.g. Chili" aria-label="Leftover name"
                 [value]="newName()" (input)="newName.set($any($event.target).value)" />
          <label class="inline-label">Portions
            <input class="narrow" type="number" min="1" max="50" [value]="newPortions()" (input)="newPortions.set(+$any($event.target).value)" />
          </label>
          <select aria-label="Where it's kept" [value]="newLocation()" (change)="newLocation.set($any($event.target).value)">
            <option value="fridge">Fridge</option>
            <option value="freezer">Freezer</option>
          </select>
          <label class="inline-label">Made
            <input type="date" [max]="today" [value]="newMadeOn()" (input)="newMadeOn.set($any($event.target).value)" />
          </label>
          <button type="button" class="primary" (click)="addLeftover()" [disabled]="busy() || !newName().trim() || newPortions() < 1">Add</button>
        </div>
      </details>
    </section>

    <section class="card kitchen-section">
      <h2>Pantry staples</h2>
      <p class="small-text muted">Things you always have. Adding a recipe or meal plan to your shopping list leaves these off.</p>
      @if (pantry().length) {
        <ul class="chips pantry-chips">
          @for (p of pantry(); track p.id) {
            <li class="chip">{{ p.name }}
              <button type="button" class="link" (click)="removeStaple(p)" [attr.aria-label]="'Remove ' + p.name">×</button>
            </li>
          }
        </ul>
      }
      <div class="row">
        <input class="grow" maxlength="100" list="pantry-ingredients" placeholder="Add a staple, e.g. olive oil" aria-label="Pantry staple"
               [value]="stapleName()" (input)="stapleName.set($any($event.target).value)" (keydown.enter)="addStaple()" />
        <button type="button" (click)="addStaple()" [disabled]="busy() || !stapleName().trim()">Add</button>
        @if (missingCommon().length) {
          <button type="button" class="link" (click)="addCommon()" [disabled]="busy()">Add common staples ({{ missingCommon().length }})</button>
        }
      </div>
      <datalist id="pantry-ingredients">
        @for (i of known(); track i.id) { <option [value]="i.name"></option> }
      </datalist>
    </section>
  `
})
export class KitchenPage implements OnInit {
  private api = inject(ApiService);
  readonly today = toIsoDate(new Date());

  leftovers = signal<Leftover[]>([]);
  pantry = signal<PantryItem[]>([]);
  known = signal<Ingredient[]>([]);
  loaded = signal(false);
  busy = signal(false);
  error = signal<string | null>(null);

  newName = signal('');
  newPortions = signal(2);
  newLocation = signal<LeftoverLocation>('fridge');
  newMadeOn = signal(this.today);
  stapleName = signal('');

  portions = portionsLabel;
  fresh = (l: Leftover) => freshness(l, this.today);
  missingCommon = computed(() => {
    const have = new Set(this.pantry().map(p => p.name.toLowerCase()));
    return COMMON_STAPLES.filter(s => !have.has(s.toLowerCase()));
  });

  async ngOnInit() {
    await this.run(async () => {
      const [leftovers, pantry] = await Promise.all([
        firstValueFrom(this.api.getLeftovers()),
        firstValueFrom(this.api.getPantry())
      ]);
      this.leftovers.set(leftovers);
      this.pantry.set(pantry);
    });
    this.loaded.set(true);
    void firstValueFrom(this.api.getIngredients()).then(i => this.known.set(i)).catch(() => {});
  }

  shortDate(iso: string) {
    const [y, m, d] = iso.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }

  private sortLeftovers(list: Leftover[]) {
    return [...list].sort((a, b) => a.useBy.localeCompare(b.useBy) || a.name.localeCompare(b.name));
  }

  async addLeftover() {
    await this.run(async () => {
      const created = await firstValueFrom(this.api.addLeftover({
        name: this.newName().trim(), recipeId: null, portions: this.newPortions(),
        location: this.newLocation(), madeOn: this.newMadeOn() || this.today
      }));
      this.leftovers.update(list => this.sortLeftovers([...list, created]));
      this.newName.set('');
      this.newPortions.set(2);
      this.newMadeOn.set(this.today);
    });
  }

  async eat(l: Leftover) {
    await this.run(async () => {
      const after = await firstValueFrom(this.api.eatLeftover(l.id));
      this.leftovers.update(list => after ? list.map(x => x.id === l.id ? after : x) : list.filter(x => x.id !== l.id));
    });
  }

  async move(l: Leftover, location: LeftoverLocation) {
    await this.run(async () => {
      const after = await firstValueFrom(this.api.updateLeftover(l.id, { location, on: this.today }));
      this.leftovers.update(list => this.sortLeftovers(list.map(x => x.id === l.id ? after : x)));
    });
  }

  async remove(l: Leftover) {
    await this.run(async () => {
      await firstValueFrom(this.api.removeLeftover(l.id));
      this.leftovers.update(list => list.filter(x => x.id !== l.id));
    });
  }

  async addStaple() {
    const name = this.stapleName().trim();
    if (!name) return;
    await this.run(async () => {
      this.pantry.set(await firstValueFrom(this.api.addToPantry([name])));
      this.stapleName.set('');
    });
  }

  async addCommon() {
    await this.run(async () => this.pantry.set(await firstValueFrom(this.api.addToPantry(this.missingCommon()))));
  }

  async removeStaple(p: PantryItem) {
    await this.run(async () => {
      await firstValueFrom(this.api.removeFromPantry(p.id));
      this.pantry.update(list => list.filter(x => x.id !== p.id));
    });
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (e) {
      const detail = e instanceof HttpErrorResponse && e.status === 400
        ? Object.values((e.error?.errors ?? {}) as Record<string, string[]>).flat()[0] : null;
      this.error.set(detail ?? 'Something went wrong. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}

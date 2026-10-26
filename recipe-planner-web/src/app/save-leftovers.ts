import { Component, inject, input, linkedSignal, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { Leftover, LeftoverLocation } from './models';
import { toIsoDate } from './date-utils';
import { portionsLabel } from './kitchen';

// "Save 3 portions to the fridge": tracks a recipe's leftovers on the Kitchen page
@Component({
  selector: 'app-save-leftovers',
  imports: [RouterLink],
  template: `
    @if (saved(); as s) {
      <p class="small-text" role="status">Saved {{ label(s.portions) }} to the {{ s.location }}. <a routerLink="/kitchen">Kitchen</a></p>
    } @else {
      <div class="save-leftovers">
        <label class="inline-label">{{ prompt() }}
          <input type="number" min="1" max="50" [value]="portions()" (input)="portions.set(+$any($event.target).value)" aria-label="Leftover portions" />
        </label>
        <select [value]="location()" (change)="location.set($any($event.target).value)" aria-label="Where you'll keep them">
          <option value="fridge">Fridge</option>
          <option value="freezer">Freezer</option>
        </select>
        <button type="button" (click)="save()" [disabled]="busy() || portions() < 1 || portions() > 50">Save leftovers</button>
      </div>
      @if (error()) { <p class="error small-text" role="alert">{{ error() }}</p> }
    }
  `
})
export class SaveLeftovers {
  private api = inject(ApiService);

  recipeId = input.required<number>();
  defaultPortions = input(2);
  defaultLocation = input<LeftoverLocation>('fridge');
  madeOn = input<string | null>(null);
  prompt = input('Portions left');
  done = output<Leftover>();

  portions = linkedSignal(() => Math.min(Math.max(this.defaultPortions(), 1), 50));
  location = linkedSignal(() => this.defaultLocation());
  saved = signal<Leftover | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);
  label = portionsLabel;

  async save() {
    this.busy.set(true);
    this.error.set(null);
    try {
      const leftover = await firstValueFrom(this.api.addLeftover({
        name: null, recipeId: this.recipeId(), portions: this.portions(),
        location: this.location(), madeOn: this.madeOn() ?? toIsoDate(new Date())
      }));
      this.saved.set(leftover);
      this.done.emit(leftover);
    } catch {
      this.error.set("Couldn't save the leftovers. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}

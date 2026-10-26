import { Component, computed, input, output } from '@angular/core';
import { MAX_SERVINGS, MIN_SERVINGS, clampServings } from './scaling';

// "Servings  −  6  +   (recipe makes 4 · Reset)". Changing it rescales the ingredient list.
@Component({
  selector: 'app-servings-stepper',
  template: `
    <div class="servings-stepper" role="group" aria-label="Servings">
      <span class="stepper-label">Servings</span>
      <button type="button" class="step" (click)="set(value() - 1)" [disabled]="value() <= MIN" aria-label="Fewer servings">−</button>
      <input type="number" [min]="MIN" [max]="MAX" [value]="value()" aria-label="Number of servings"
             (change)="set(+$any($event.target).value)" />
      <button type="button" class="step" (click)="set(value() + 1)" [disabled]="value() >= MAX" aria-label="More servings">+</button>
      @if (changed()) {
        <span class="muted small-text">Recipe makes {{ base() }} ·
          <button type="button" class="link" (click)="set(base())">Reset</button></span>
      }
    </div>
  `
})
export class ServingsStepper {
  readonly MIN = MIN_SERVINGS;
  readonly MAX = MAX_SERVINGS;

  value = input.required<number>();
  base = input.required<number>();
  valueChange = output<number>();

  changed = computed(() => this.value() !== this.base());

  set(n: number) {
    const next = clampServings(n);
    this.valueChange.emit(next);
  }
}

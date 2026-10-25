import { Component, computed, input, output, signal } from '@angular/core';

// Five clickable stars. Keyboard and screen-reader friendly: each star is a real button with a label.
@Component({
  selector: 'app-star-rating',
  template: `
    <span class="stars" [class.readonly]="readonly()" role="group" [attr.aria-label]="label()">
      @for (n of [1, 2, 3, 4, 5]; track n) {
        @if (readonly()) {
          <span class="star" [class.on]="n <= shown()" aria-hidden="true">★</span>
        } @else {
          <button type="button" class="star" [class.on]="n <= shown()"
                  [disabled]="disabled()"
                  [attr.aria-label]="n + (n === 1 ? ' star' : ' stars')"
                  [attr.aria-pressed]="value() === n"
                  (mouseenter)="hover.set(n)" (mouseleave)="hover.set(0)" (focus)="hover.set(n)" (blur)="hover.set(0)"
                  (click)="rate.emit(n)">★</button>
        }
      }
    </span>
  `
})
export class StarRating {
  value = input<number | null>(null);   // whole stars for an editable rating, or an average for read-only
  readonly = input(false);
  disabled = input(false);
  label = input('Rating');
  rate = output<number>();

  hover = signal(0);
  shown = computed(() => this.hover() || Math.round(this.value() ?? 0));
}

import { Component, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { Technique } from './models';

// The technique list is small and rarely changes, so it's fetched once and shared by every chip
let allTechniques: Promise<Technique[]> | null = null;

/** A technique name you can tap to read how it's done, without leaving the recipe */
@Component({
  selector: 'app-technique-chip',
  imports: [RouterLink],
  template: `
    <button type="button" class="chip technique-chip" [class.small]="small()" (click)="open()"
            [attr.aria-label]="'How to ' + name() + ': open technique details'">
      {{ name() }}@if (difficulty() !== null) { · {{ difficulty() }}/5 }
    </button>

    <dialog #dialog class="technique-dialog" (click)="backdropClose($event)">
      <div class="technique-dialog-body">
        <div class="technique-dialog-head">
          <h2>{{ technique()?.name ?? name() }}</h2>
          <button type="button" class="link close" (click)="close()" aria-label="Close">✕</button>
        </div>
        @if (technique(); as t) {
          <p class="muted small-text">Difficulty {{ t.difficulty }}/5</p>
          <p>{{ t.summary }}</p>
          @if (t.donenessCues) { <p><strong>Done when:</strong> {{ t.donenessCues }}</p> }
          @if (t.commonMistakes) { <p><strong>Common mistakes:</strong> {{ t.commonMistakes }}</p> }
          <a routerLink="/techniques" class="small-text" (click)="close()">All techniques</a>
        } @else if (failed()) {
          <p class="muted">Couldn't find details for this technique. <a routerLink="/techniques" (click)="close()">Browse all techniques</a></p>
        } @else {
          <p class="muted">Loading…</p>
        }
      </div>
    </dialog>
  `
})
export class TechniqueChip {
  private api = inject(ApiService);
  name = input.required<string>();
  /** Shown after the name when given, like "Braise · 3/5" */
  difficulty = input<number | null>(null);
  small = input(false);

  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  technique = signal<Technique | null>(null);
  failed = signal(false);

  async open() {
    this.dialog().nativeElement.showModal();
    if (this.technique()) return;
    this.failed.set(false);
    try {
      allTechniques ??= firstValueFrom(this.api.getTechniques());
      const key = this.name().trim().toLowerCase();
      const found = (await allTechniques).find(t => t.name.trim().toLowerCase() === key);
      if (found) this.technique.set(found); else this.failed.set(true);
    } catch {
      allTechniques = null;   // try again next time
      this.failed.set(true);
    }
  }

  close() {
    this.dialog().nativeElement.close();
  }

  // A click on the dimmed area around the box (the dialog element itself, not its contents) closes it
  backdropClose(event: MouseEvent) {
    if (event.target === this.dialog().nativeElement) this.close();
  }
}

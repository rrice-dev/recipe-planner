import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-technique-list',
  template: `
    <h1>Technique Library</h1>
    @if (techniques(); as list) {
      <div class="grid">
        @for (t of list; track t.id) {
          <article class="card">
            <h2>{{ t.name }} <span class="chip small">{{ t.difficulty }}/5</span></h2>
            <p>{{ t.summary }}</p>
            @if (t.donenessCues) {
              <p><strong>Done when:</strong> {{ t.donenessCues }}</p>
            }
            @if (t.commonMistakes) {
              <p><strong>Common mistakes:</strong> {{ t.commonMistakes }}</p>
            }
          </article>
        } @empty {
          <p class="muted">No techniques yet.</p>
        }
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class TechniqueListPage {
  private api = inject(ApiService);
  techniques = toSignal(this.api.getTechniques());
}
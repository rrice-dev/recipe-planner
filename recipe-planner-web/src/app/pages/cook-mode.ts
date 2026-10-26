import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { SettingsService } from '../settings.service';
import { FoundTime, findTimes, formatClock } from '../cook-timers';
import { formatMeasure, scaleQuantity } from '../scaling';
import { SaveLeftovers } from '../save-leftovers';
import { ServingsStepper } from '../servings-stepper';
import { toIsoDate } from '../date-utils';

interface CookRecipe {
  title: string;
  servings: number;
  backLink: string[];
  ingredients: { name: string; quantity: number; unit: string; note: string | null }[];
  steps: { instruction: string; technique: string | null }[];
}

interface Timer {
  id: number;
  label: string;
  step: number;
  total: number;
  endsAt: number | null;   // null while paused
  remaining: number;       // seconds left when paused
}

// Full-screen, one step at a time. Big type, timers found in the steps, and the screen stays on.
@Component({
  selector: 'app-cook-mode',
  imports: [RouterLink, ServingsStepper, SaveLeftovers],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    <div class="cook" role="dialog" aria-modal="true" [attr.aria-label]="'Cooking ' + (recipe()?.title ?? '')">
      <div>
        <div class="cook-top">
          <a [routerLink]="recipe()?.backLink ?? ['/recipes']" class="button" aria-label="Leave cook mode">✕ Exit</a>
          <span class="title">{{ recipe()?.title ?? 'Loading…' }}</span>
          <button type="button" (click)="showIngredients.set(!showIngredients())" [attr.aria-expanded]="showIngredients()">Ingredients</button>
        </div>
        <div class="cook-progress" aria-hidden="true"><span [style.width.%]="progress()"></span></div>
      </div>

      @if (recipe(); as r) {
        <div class="cook-stage" (pointerdown)="swipeStart($event)" (pointerup)="swipeEnd($event)">
          @if (r.steps.length) {
            <p class="cook-step-count" aria-live="polite">Step {{ index() + 1 }} of {{ r.steps.length }}</p>
            <p class="cook-instruction">{{ step()!.instruction }}</p>
            @if (step()!.technique) {
              <div class="cook-technique"><span class="chip">{{ step()!.technique }}</span></div>
            }

            <div class="cook-timers">
              @for (t of stepTimes(); track t.seconds) {
                @if (!hasTimer(t)) {
                  <button type="button" (click)="startTimer(t)">⏱ Start {{ t.label }} timer</button>
                }
              }
              @for (t of timers(); track t.id) {
                <div class="timer" [class.done]="left(t) === 0" role="timer" [attr.aria-label]="t.label + ' timer'">
                  <svg class="timer-ring" viewBox="0 0 40 40" aria-hidden="true">
                    <circle class="track" cx="20" cy="20" r="16" />
                    <circle class="left" cx="20" cy="20" r="16" [attr.stroke-dasharray]="CIRC" [attr.stroke-dashoffset]="CIRC * (1 - left(t) / t.total)" />
                  </svg>
                  <div>
                    <div class="timer-time">{{ left(t) === 0 ? 'Done' : clock(left(t)) }}</div>
                    <div class="timer-label">Step {{ t.step + 1 }} · {{ t.label }}</div>
                  </div>
                  @if (left(t) > 0) {
                    <button type="button" (click)="togglePause(t)">{{ t.endsAt ? 'Pause' : 'Resume' }}</button>
                  }
                  <button type="button" (click)="removeTimer(t)" [attr.aria-label]="'Dismiss ' + t.label + ' timer'">{{ left(t) === 0 ? 'OK' : 'Cancel' }}</button>
                </div>
              }
            </div>
            @if (id() && index() === r.steps.length - 1) {
              <div class="cook-leftovers">
                <app-save-leftovers [recipeId]="+id()!" [defaultPortions]="(servings() ?? r.servings) - 1" prompt="Any leftovers? Portions" />
              </div>
            }
          } @else {
            <p class="cook-instruction">This recipe has no steps yet.</p>
          }
        </div>

        <div class="cook-bottom">
          <button type="button" (click)="go(-1)" [disabled]="index() === 0">Back</button>
          @if (index() < r.steps.length - 1) {
            <button type="button" class="primary" (click)="go(1)">Next step</button>
          } @else {
            @if (id()) {
              <button type="button" class="primary" (click)="finish()" [disabled]="finishing()">{{ finishing() ? 'Saving…' : 'Done, mark as cooked' }}</button>
            } @else {
              <a [routerLink]="r.backLink" class="button primary">Done cooking</a>
            }
          }
        </div>

        @if (showIngredients()) {
          <aside class="cook-drawer" aria-label="Ingredients">
            <div class="page-header">
              <h2>Ingredients</h2>
              <button type="button" (click)="showIngredients.set(false)">Close</button>
            </div>
            <app-servings-stepper [value]="servings() ?? r.servings" [base]="r.servings" (valueChange)="servings.set($event)" />
            <p class="small-text" style="color: #97A59E">Tick things off as you add them.</p>
            <ul style="list-style: none; padding: 0">
              @for (i of r.ingredients; track i.name; let n = $index) {
                <li [class.got]="got().has(n)">
                  <label><input type="checkbox" [checked]="got().has(n)" (change)="toggleGot(n)" />
                    <span>{{ measure(scale(i.quantity, r.servings), i.unit) }} {{ i.name }}@if (i.note) {<span style="color: #97A59E">, {{ i.note }}</span>}</span>
                  </label>
                </li>
              }
            </ul>
            @if (wakeLockActive()) { <p class="cook-awake">Your screen will stay on while cook mode is open.</p> }
          </aside>
        }
      } @else if (failed()) {
        <div class="cook-stage"><p class="cook-instruction">Couldn't load this recipe.</p></div>
      }
    </div>
  `
})
export class CookModePage implements OnInit {
  private api = inject(ApiService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  id = input<string>();
  slug = input<string>();

  readonly CIRC = 2 * Math.PI * 16;
  clock = formatClock;
  private unitSystem = inject(SettingsService).settings;
  // Amounts in your chosen units (Settings → Units)
  measure = (quantity: number, unit: string) => formatMeasure(quantity, unit, this.unitSystem().units);

  recipe = signal<CookRecipe | null>(null);
  failed = signal(false);
  index = signal(0);
  showIngredients = signal(false);
  servings = signal<number | null>(null);
  got = signal(new Set<number>());
  timers = signal<Timer[]>([]);
  now = signal(Date.now());
  wakeLockActive = signal(false);
  finishing = signal(false);

  step = computed(() => this.recipe()?.steps[this.index()] ?? null);
  stepTimes = computed(() => findTimes(this.step()?.instruction ?? ''));
  progress = computed(() => {
    const total = this.recipe()?.steps.length ?? 0;
    return total ? ((this.index() + 1) / total) * 100 : 0;
  });

  private nextTimerId = 1;
  private wakeLock: WakeLockSentinel | null = null;
  private audio: AudioContext | null = null;
  private announced = new Set<number>();

  async ngOnInit() {
    try {
      this.recipe.set(await this.loadRecipe());
    } catch {
      this.failed.set(true);
    }

    const tick = setInterval(() => this.tick(), 250);
    const onVisible = () => { if (document.visibilityState === 'visible') void this.keepAwake(); };
    document.addEventListener('visibilitychange', onVisible);
    void this.keepAwake();

    this.destroyRef.onDestroy(() => {
      clearInterval(tick);
      document.removeEventListener('visibilitychange', onVisible);
      void this.wakeLock?.release().catch(() => {});
      void this.audio?.close().catch(() => {});
    });
  }

  private async loadRecipe(): Promise<CookRecipe> {
    const slug = this.slug();
    if (slug) {
      const r = await firstValueFrom(this.api.getLibraryRecipe(slug));
      return { title: r.title, servings: r.servings, backLink: ['/library', r.slug], ingredients: r.ingredients,
        steps: r.steps.map(s => ({ instruction: s.instruction, technique: s.technique })) };
    }
    const r = await firstValueFrom(this.api.getRecipe(Number(this.id())));
    return {
      title: r.title, servings: r.servings, backLink: ['/recipes', String(r.id)],
      ingredients: r.ingredients.map(i => ({ name: i.name, quantity: i.quantity, unit: i.unit, note: i.note })),
      steps: [...r.steps].sort((a, b) => a.order - b.order).map(s => ({ instruction: s.instruction, technique: s.techniqueName ?? null }))
    };
  }

  // The Screen Wake Lock API keeps phones and tablets from dimming mid-recipe. Browsers drop the lock
  // when the tab is hidden, so it's requested again when you come back.
  private async keepAwake() {
    try {
      if ('wakeLock' in navigator && !this.wakeLock) {
        this.wakeLock = await navigator.wakeLock.request('screen');
        this.wakeLockActive.set(true);
        this.wakeLock.addEventListener('release', () => { this.wakeLock = null; this.wakeLockActive.set(false); });
      }
    } catch {
      this.wakeLockActive.set(false); // not supported, or denied (e.g. low battery mode)
    }
  }

  // Logs today in your cooking history, then goes back to the recipe
  async finish() {
    this.finishing.set(true);
    try {
      await firstValueFrom(this.api.markCooked(Number(this.id()), toIsoDate(new Date())));
    } catch {
      // Not worth blocking on: the recipe page has an "I cooked this" button too
    } finally {
      this.finishing.set(false);
      await this.router.navigate(this.recipe()?.backLink ?? ['/recipes']);
    }
  }

  go(delta: number) {
    const total = this.recipe()?.steps.length ?? 0;
    this.index.update(i => Math.min(Math.max(i + delta, 0), Math.max(total - 1, 0)));
  }

  onKey(e: KeyboardEvent) {
    // Escape always works: closes the ingredients panel first, then leaves cook mode
    if (e.key === 'Escape') {
      if (this.showIngredients()) this.showIngredients.set(false);
      else void this.router.navigate(this.recipe()?.backLink ?? ['/recipes']);
      return;
    }
    // Arrows and space change steps, except while you're using a form control or button
    if ((e.target as HTMLElement)?.closest('input, select, textarea, button, a')) return;
    if (e.key === 'ArrowRight' || e.key === ' ') { e.preventDefault(); this.go(1); }
    else if (e.key === 'ArrowLeft') { e.preventDefault(); this.go(-1); }
  }

  // Swipe left/right on the step to move, for messy hands
  private swipeFrom: { x: number; y: number } | null = null;
  swipeStart(e: PointerEvent) { this.swipeFrom = { x: e.clientX, y: e.clientY }; }
  swipeEnd(e: PointerEvent) {
    if (!this.swipeFrom || (e.target as HTMLElement).closest('button, input, select, label')) return;
    const dx = e.clientX - this.swipeFrom.x, dy = e.clientY - this.swipeFrom.y;
    this.swipeFrom = null;
    if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) this.go(dx < 0 ? 1 : -1);
  }

  scale(quantity: number, base: number) {
    return scaleQuantity(quantity, base, this.servings() ?? base);
  }

  toggleGot(n: number) {
    this.got.update(s => { const next = new Set(s); if (next.has(n)) next.delete(n); else next.add(n); return next; });
  }

  // ---------- Timers ----------

  hasTimer(t: FoundTime) {
    return this.timers().some(x => x.step === this.index() && x.total === t.seconds);
  }

  startTimer(t: FoundTime) {
    // Browsers only allow sound after a tap, so set up audio now for the alarm later
    try { this.audio ??= new AudioContext(); void this.audio.resume(); } catch { /* no sound available */ }
    this.timers.update(list => [...list, {
      id: this.nextTimerId++, label: t.label, step: this.index(), total: t.seconds,
      endsAt: Date.now() + t.seconds * 1000, remaining: t.seconds
    }]);
  }

  left(t: Timer) {
    return t.endsAt ? Math.max(0, (t.endsAt - this.now()) / 1000) : t.remaining;
  }

  togglePause(t: Timer) {
    this.timers.update(list => list.map(x => x.id !== t.id ? x
      : x.endsAt ? { ...x, endsAt: null, remaining: this.left(x) }
      : { ...x, endsAt: Date.now() + x.remaining * 1000 }));
  }

  removeTimer(t: Timer) {
    this.timers.update(list => list.filter(x => x.id !== t.id));
  }

  private tick() {
    this.now.set(Date.now());
    for (const t of this.timers()) {
      if (t.endsAt && this.left(t) === 0 && !this.announced.has(t.id)) {
        this.announced.add(t.id);
        this.alarm();
      }
    }
  }

  private alarm() {
    try { navigator.vibrate?.([300, 150, 300, 150, 300]); } catch { /* not supported */ }
    const ctx = this.audio;
    if (!ctx) return;
    // Three short beeps
    for (let i = 0; i < 3; i++) {
      const osc = ctx.createOscillator(), gain = ctx.createGain();
      const start = ctx.currentTime + i * 0.45;
      osc.frequency.value = 880;
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.exponentialRampToValueAtTime(0.4, start + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.3);
      osc.connect(gain).connect(ctx.destination);
      osc.start(start);
      osc.stop(start + 0.32);
    }
  }
}

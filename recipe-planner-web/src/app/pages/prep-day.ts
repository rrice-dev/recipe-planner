import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { PrepSession } from '../models';
import { SettingsService } from '../settings.service';
import { formatMeasure } from '../scaling';
import { parseDate, toIsoDate } from '../date-utils';
import { totalTime } from '../library-utils';
import { SaveLeftovers } from '../save-leftovers';

const APPROACH_LABEL = { CookAhead: 'Cook today', PrepAheadCookFresh: 'Prep today, cook that night', Freeze: 'Cook today and freeze' } as const;

// One session to get the week ready: what to cook, what to start first, what to chop once.
@Component({
  selector: 'app-prep-day',
  imports: [RouterLink, SaveLeftovers],
  template: `
    <a [routerLink]="['/meal-plans', planId()]" class="back no-print">← Meal plan</a>
    @if (session(); as s) {
      <div class="page-header">
        <h1>Prep day: {{ s.name }}</h1>
        <button type="button" class="no-print" (click)="print()">Print checklist</button>
      </div>
      <div class="row no-print">
        <label class="inline-label">Prep day <input type="date" [value]="s.prepDay" (change)="load($any($event.target).value)" /></label>
        <span class="small-text muted">{{ done().size }} of {{ total() }} done</span>
      </div>

      <h2>Your week</h2>
      <ol class="plan-days">
        @for (m of s.meals; track m.day + m.recipeId) {
          <li class="plan-day prep-meal" [attr.data-approach]="m.approach">
            <span class="day-name">{{ weekday(m.day) }}<span class="day-date">{{ shortDate(m.day) }}</span></span>
            <div class="dish">
              <strong><a [routerLink]="['/recipes', m.recipeId]" class="plain">{{ m.title }}</a></strong>
              <span class="source-mark">{{ m.reason }}</span>
              @if (m.approach !== 'PrepAheadCookFresh' && s.prepDay <= today) {
                <div class="no-print">
                  <app-save-leftovers [recipeId]="m.recipeId" [defaultPortions]="m.servings" [madeOn]="s.prepDay"
                                      [defaultLocation]="m.approach === 'Freeze' ? 'freezer' : 'fridge'" prompt="Cooked it? Portions" />
                </div>
              }
            </div>
            <span class="chip small approach">{{ label(m.approach) }}</span>
          </li>
        } @empty {
          <li class="muted">This plan has no meals yet.</li>
        }
      </ol>

      @if (s.startFirst.length) {
        <h2>Start these first</h2>
        <p class="small-text muted">The longest cooks go on first, so they're done while you prep everything else.</p>
        <ul class="prep-list">
          @for (l of s.startFirst; track l.recipeId) {
            <li><label><input type="checkbox" [checked]="done().has('start:' + l.recipeId)" (change)="toggle('start:' + l.recipeId)" />
              <span><strong>{{ l.title }}</strong> ({{ time(l.minutes) }} to cook). {{ l.firstStep }}</span></label></li>
          }
        </ul>
      }

      @if (s.chopOnce.length) {
        <h2>Chop and prep once</h2>
        <p class="small-text muted">Everything the week needs, combined, so each ingredient gets one trip to the cutting board.</p>
        <ul class="prep-list">
          @for (c of s.chopOnce; track c.ingredient + c.prep + c.unit) {
            <li><label><input type="checkbox" [checked]="done().has(key(c))" (change)="toggle(key(c))" />
              <span><strong>{{ measure(c.quantity, c.unit) }} {{ c.ingredient }}</strong>, {{ c.prep }}
                <span class="muted small-text">for {{ c.forRecipes.join(', ') }}</span></span></label></li>
          }
        </ul>
      }

      @if (s.marinate.length) {
        <h2>Marinate</h2>
        <ul class="prep-list">
          @for (t of s.marinate; track t) {
            <li><label><input type="checkbox" [checked]="done().has('marinate:' + t)" (change)="toggle('marinate:' + t)" />
              <span>{{ t }}: mix the marinade and get it in the fridge.</span></label></li>
          }
        </ul>
      }

      <h2>Store it safely</h2>
      <ul class="small-text">
        <li>Cool cooked food and get it in the fridge within 2 hours. Shallow containers cool fastest.</li>
        <li>Cooked food keeps 3 to 4 days in the fridge. Label containers with the day.</li>
        <li>Freeze anything for later in the week, then thaw it in the fridge the day before.</li>
        <li>Reheat leftovers until steaming hot (165°F / 74°C).</li>
      </ul>
    } @else if (error()) {
      <p class="error">{{ error() }}</p>
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class PrepDayPage implements OnInit {
  private api = inject(ApiService);
  private units = inject(SettingsService).settings;
  id = input.required<string>();

  session = signal<PrepSession | null>(null);
  error = signal<string | null>(null);
  done = signal(new Set<string>());
  time = totalTime;
  readonly today = toIsoDate(new Date());

  planId = computed(() => Number(this.id()));
  total = computed(() => {
    const s = this.session();
    return s ? s.startFirst.length + s.chopOnce.length + s.marinate.length : 0;
  });

  measure = (q: number, unit: string) => formatMeasure(q, unit, this.units().units);
  label = (a: keyof typeof APPROACH_LABEL) => APPROACH_LABEL[a];
  key = (c: { ingredient: string; prep: string; unit: string }) => `chop:${c.ingredient}|${c.prep}|${c.unit}`;

  ngOnInit() {
    void this.load(null);
  }

  async load(prepDay: string | null) {
    this.error.set(null);
    try {
      const s = await firstValueFrom(this.api.getPrepSession(this.planId(), prepDay));
      this.session.set(s);
      this.done.set(this.readChecks(s));
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 400 ? 'Pick a prep day close to the planned week.'
        : e instanceof HttpErrorResponse && e.status === 404 ? 'That meal plan was not found.'
        : "Couldn't load the prep plan. Please try again.");
    }
  }

  // Ticked items are remembered on this device, so you can leave and come back mid-prep
  private storageKey(s: PrepSession) { return `rp-prep:${s.mealPlanId}:${s.prepDay}`; }

  private readChecks(s: PrepSession): Set<string> {
    try { return new Set(JSON.parse(localStorage.getItem(this.storageKey(s)) ?? '[]')); } catch { return new Set(); }
  }

  toggle(k: string) {
    const next = new Set(this.done());
    if (next.has(k)) next.delete(k); else next.add(k);
    this.done.set(next);
    const s = this.session();
    try { if (s) localStorage.setItem(this.storageKey(s), JSON.stringify([...next])); } catch { /* storage blocked: still works for this visit */ }
  }

  weekday(iso: string) { return parseDate(iso).toLocaleDateString(undefined, { weekday: 'long' }); }
  shortDate(iso: string) { return parseDate(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }); }
  print() { window.print(); }
}

import { Component, ElementRef, computed, inject, linkedSignal, signal, viewChild } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, firstValueFrom, map, of } from 'rxjs';
import { geoNaturalEarth1, geoPath } from 'd3-geo';
import { feature } from 'topojson-client';
import type { Topology, GeometryCollection } from 'topojson-specification';
import type { Feature, FeatureCollection, Geometry } from 'geojson';
import { ApiService } from '../api.service';
import { TechniqueChip } from '../technique-chip';
import { AuthService } from '../auth.service';
import { CONFIRM_EMAIL_MESSAGE, ConfirmEmailNotice, needsConfirmedEmail } from '../confirm-email';
import { DraftSaver } from '../draft-saver';
import { SettingsService } from '../settings.service';
import { CountryDish, LibrarySummary, RecipeDraft } from '../models';
import { totalTime } from '../library-utils';
import { formatMeasure } from '../scaling';
import { EXTRA_PLACES, NOT_SELECTABLE, displayName } from '../world-names';

const WIDTH = 960;
const HEIGHT = 470;
const MAX_ZOOM = 10;

interface Country { name: string; label: string; d: string; bounds: [[number, number], [number, number]]; }
interface ViewBox { x: number; y: number; w: number; h: number; }

@Component({
  selector: 'app-world-map',
  imports: [TechniqueChip, RouterLink, ConfirmEmailNotice],
  template: `
    <div class="page-header">
      <h1>Recipes Around the World</h1>
      <a routerLink="/library" class="button">List view</a>
    </div>
    <p class="muted">Click a country to see its recipes. Shaded countries have recipes in the library; for any country,
      you can also ask for AI suggestions.</p>
    <app-confirm-email-notice />

    <div class="library-filters">
      <input type="search" list="world-countries" placeholder="Find a country" aria-label="Find a country"
             [value]="search()" (input)="onSearch($any($event.target).value)" (keydown.enter)="onSearch($any($event.target).value, true)" />
      <datalist id="world-countries">
        @for (c of searchable(); track c.name) { <option [value]="c.label"></option> }
      </datalist>
      <label class="check-label"><input type="checkbox" [checked]="aipOnly()" (change)="aipOnly.set($any($event.target).checked)" /> AIP only</label>
    </div>

    <div class="map-wrap">
      @if (countries(); as list) {
        <svg #svg class="world-map" [class.zoomed]="zoomed()" [attr.viewBox]="viewBoxAttr()" role="group" aria-label="World map"
             (wheel)="onWheel($event)" (pointerdown)="onPointerDown($event)" (pointermove)="onPointerMove($event)"
             (pointerup)="onPointerUp($event)" (pointercancel)="onPointerUp($event)">
          <rect [attr.width]="WIDTH" [attr.height]="HEIGHT" class="ocean" />
          @for (c of list; track c.name) {
            <path [attr.d]="c.d" class="country"
                  [class.has-recipes]="counts().has(c.name)"
                  [class.selected]="selected() === c.name"
                  [class.inert]="isInert(c.name)"
                  [attr.vector-effect]="'non-scaling-stroke'"
                  (click)="clickCountry(c.name)">
              <title>{{ c.label }}{{ counts().has(c.name) ? ' · ' + counts().get(c.name) + ' recipes' : '' }}</title>
            </path>
          }
        </svg>
        <div class="map-controls">
          <button type="button" (click)="zoomBy(1.6)" aria-label="Zoom in">+</button>
          <button type="button" (click)="zoomBy(1 / 1.6)" aria-label="Zoom out">−</button>
          <button type="button" (click)="resetView()" aria-label="Show the whole world">⟲</button>
        </div>
        <p class="muted small-text map-legend"><span class="swatch"></span> Has library recipes · Drag to pan, scroll or pinch to zoom</p>
      } @else if (mapFailed()) {
        <p class="error">Couldn't load the map. You can still search for a country above.</p>
      } @else {
        <p class="muted">Loading map…</p>
      }
    </div>

    @if (selected(); as country) {
      <section class="country-panel" id="country-panel">
        <h2>{{ label(country) }}</h2>

        @if (libraryFor(country); as recipes) {
          @if (recipes.length) {
            <h3>From the library</h3>
            <div class="grid">
              @for (r of recipes; track r.slug) {
                <a class="card" [routerLink]="['/library', r.slug]">
                  <strong>{{ r.title }}</strong>
                  <p class="meta small-text">{{ time(r.prepMinutes + r.cookMinutes) }}, serves {{ r.servings }}</p>
                  <p class="small-text">{{ r.description }}</p>
                  <div class="chips">
                    @for (t of r.tags; track t) { <span class="chip small tag-{{ t.toLowerCase() }}">{{ t }}</span> }
                  </div>
                </a>
              }
            </div>
          } @else {
            <p class="muted">No {{ aipOnly() ? 'AIP ' : '' }}library recipes from {{ label(country) }} yet.</p>
          }
        }

        <h3>More dishes from {{ label(country) }}</h3>
        @if (!auth.isLoggedIn()) {
          <p class="small-text"><a [routerLink]="['/login']" [queryParams]="{ returnUrl: '/world' }">Log in</a> to get AI suggestions for any country.</p>
        } @else if (dishes(); as d) {
          @if (d.notice) { <p class="muted">{{ d.notice }}</p> }
          <div class="grid">
            @for (dish of visibleDishes(); track dish.name) {
              <article class="card">
                <strong>{{ dish.name }}</strong>
                @if (dish.aipFriendly) { <span class="chip small tag-aip">May be AIP</span> }
                <p class="small-text">{{ dish.description }}</p>
                <button type="button" (click)="openRecipe(country, dish)" [disabled]="loadingDish() !== null">
                  {{ loadingDish() === dish.name ? 'Writing recipe…' : 'Get the recipe' }}
                </button>
              </article>
            }
          </div>
          @if (aipOnly() && d.dishes.length && !visibleDishes().length) {
            <p class="muted small-text">None of these looked AIP-friendly. Turn off "AIP only" to see them all.</p>
          }
        } @else {
          <button type="button" class="primary" (click)="suggest(country)" [disabled]="suggesting()">
            {{ suggesting() ? 'Thinking…' : '✨ Suggest dishes from ' + label(country) }}
          </button>
        }
        @if (aiError()) { <p class="error">{{ aiError() }}</p> }

        @if (draft(); as r) {
          <article class="card recipe-draft" id="world-draft">
            <div class="page-header">
              <h2>{{ r.title }}</h2>
              <button type="button" class="link" (click)="draft.set(null)" aria-label="Close recipe">Close ×</button>
            </div>
            @if (r.description) { <p>{{ r.description }}</p> }
            <p class="meta">Serves {{ r.servings }} · Prep {{ r.prepMinutes }} min · Cook {{ r.cookMinutes }} min</p>
            <h3>Ingredients</h3>
            <ul>
              @for (i of r.ingredients; track i.name) {
                <li>{{ measure(i.quantity, i.unit) }} {{ i.name }}@if (i.note) {<span class="muted">, {{ i.note }}</span>}</li>
              }
            </ul>
            <h3>Steps</h3>
            <ol class="draft-steps">
              @for (s of r.steps; track $index) {
                <li>{{ s.instruction }}
                  @if (s.techniqueName) { <app-technique-chip [name]="s.techniqueName" [small]="true" /> }
                </li>
              }
            </ol>
            <div class="actions">
              <button type="button" class="primary" (click)="save(r, country)" [disabled]="saving()">
                {{ saving() ? 'Saving…' : 'Save to my recipes' }}
              </button>
            </div>
            @if (saveError()) { <p class="error">{{ saveError() }}</p> }
            <p class="muted small-text">AI-generated. Check cooking times, use a thermometer for meat, poultry and eggs,
              and check every ingredient yourself if you're following AIP.</p>
          </article>
        }
      </section>
    }
  `
})
export class WorldMapPage {
  private api = inject(ApiService);
  private http = inject(HttpClient);
  private router = inject(Router);
  private saver = inject(DraftSaver);
  auth = inject(AuthService);

  readonly WIDTH = WIDTH;
  readonly HEIGHT = HEIGHT;
  time = totalTime;
  private unitSystem = inject(SettingsService).settings;
  // Amounts in your chosen units (Settings → Units)
  measure = (quantity: number, unit: string) => formatMeasure(quantity, unit, this.unitSystem().units);
  label = displayName;

  svg = viewChild<ElementRef<SVGSVGElement>>('svg');
  mapFailed = signal(false);
  search = signal('');
  // On for AIP in Settings (once they load); the checkbox overrides it
  private userSettings = inject(SettingsService).settings;
  aipOnly = linkedSignal(() => this.userSettings().diet === 'AIP');
  selected = signal<string | null>(null);
  view = signal<ViewBox>({ x: 0, y: 0, w: WIDTH, h: HEIGHT });

  suggesting = signal(false);
  aiError = signal<string | null>(null);
  loadingDish = signal<string | null>(null);
  draft = signal<RecipeDraft | null>(null);
  saving = signal(false);
  saveError = signal<string | null>(null);
  private dishCache = new Map<string, { dishes: CountryDish[]; notice: string | null }>();
  private dishVersion = signal(0); // bumps when dishCache changes, so the template updates

  // ---------- Data ----------

  private library = toSignal(this.api.getLibrary().pipe(catchError(() => of([] as LibrarySummary[]))), { initialValue: [] });

  countries = toSignal(
    this.http.get<Topology<{ countries: GeometryCollection<{ name: string }> }>>('/world-110m.json').pipe(
      map(topo => {
        const all = feature(topo, topo.objects.countries) as FeatureCollection<Geometry, { name: string }>;
        const shown = all.features.filter(f => f.properties.name !== 'Antarctica');
        const projection = geoNaturalEarth1().fitExtent([[4, 4], [WIDTH - 4, HEIGHT - 4]],
          { type: 'FeatureCollection', features: shown } as FeatureCollection);
        const path = geoPath(projection);
        return shown.map((f: Feature<Geometry, { name: string }>) => ({
          name: f.properties.name,
          label: displayName(f.properties.name),
          d: path(f) ?? '',
          bounds: path.bounds(f)
        }) as Country);
      }),
      catchError(() => { this.mapFailed.set(true); return of(null); })),
    { initialValue: null });

  /** Library recipe count per country, honoring the AIP filter */
  counts = computed(() => {
    const m = new Map<string, number>();
    for (const r of this.library()) {
      if (!r.country || (this.aipOnly() && !r.tags.includes('AIP'))) continue;
      m.set(r.country, (m.get(r.country) ?? 0) + 1);
    }
    return m;
  });

  searchable = computed(() => {
    const names = (this.countries() ?? []).filter(c => !NOT_SELECTABLE.has(c.name)).map(c => ({ name: c.name, label: c.label }));
    for (const extra of EXTRA_PLACES) {
      if (!names.some(n => n.name === extra)) names.push({ name: extra, label: extra });
    }
    return names.sort((a, b) => a.label.localeCompare(b.label));
  });

  libraryFor(country: string) {
    return this.library().filter(r => r.country === country && (!this.aipOnly() || r.tags.includes('AIP')));
  }

  dishes = computed(() => {
    this.dishVersion();
    const c = this.selected();
    return c ? this.dishCache.get(c) ?? null : null;
  });

  visibleDishes = computed(() => (this.dishes()?.dishes ?? []).filter(d => !this.aipOnly() || d.aipFriendly));

  isInert(name: string) {
    return NOT_SELECTABLE.has(name);
  }

  // ---------- Choosing a country ----------

  clickCountry(name: string) {
    if (this.dragged || NOT_SELECTABLE.has(name)) return;
    this.select(name, false);
  }

  onSearch(text: string, submit = false) {
    this.search.set(text);
    const match = this.searchable().find(c => c.label.toLowerCase() === text.trim().toLowerCase()
      || c.name.toLowerCase() === text.trim().toLowerCase());
    if (match) this.select(match.name, true);
    else if (submit && text.trim()) this.aiError.set(`Couldn't find "${text.trim()}". Pick a country from the list.`);
  }

  private select(name: string, zoomTo: boolean) {
    this.selected.set(name);
    this.search.set(displayName(name));
    this.aiError.set(null);
    this.draft.set(null);
    this.saveError.set(null);
    if (zoomTo) {
      const c = this.countries()?.find(x => x.name === name);
      if (c) this.zoomToBounds(c.bounds);
    }
    setTimeout(() => document.getElementById('country-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
  }

  // ---------- AI ----------

  async suggest(country: string) {
    this.suggesting.set(true);
    this.aiError.set(null);
    try {
      const res = await firstValueFrom(this.api.getCountryDishes(displayName(country)));
      this.dishCache.set(country, { dishes: res.dishes, notice: res.aiNotice });
      this.dishVersion.update(v => v + 1);
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.aiError.set(needsConfirmedEmail(e) ? CONFIRM_EMAIL_MESSAGE : status === 429 ? "You've used your AI suggestions for this hour. Try again later." :
        "Couldn't get suggestions right now. Please try again.");
    } finally {
      this.suggesting.set(false);
    }
  }

  async openRecipe(country: string, dish: CountryDish) {
    this.loadingDish.set(dish.name);
    this.aiError.set(null);
    this.saveError.set(null);
    try {
      this.draft.set(await firstValueFrom(this.api.getCountryRecipe(displayName(country), dish.name)));
      setTimeout(() => document.getElementById('world-draft')?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.aiError.set(needsConfirmedEmail(e) ? CONFIRM_EMAIL_MESSAGE : status === 429 ? "You've opened a lot of AI recipes this hour. Try again later." :
        "Couldn't write that recipe right now. Please try again.");
    } finally {
      this.loadingDish.set(null);
    }
  }

  async save(draft: RecipeDraft, country: string) {
    this.saving.set(true);
    this.saveError.set(null);
    try {
      const id = await this.saver.save(draft, displayName(country));
      await this.router.navigate(['/recipes', id]);
    } catch {
      this.saveError.set("Couldn't save the recipe. Please try again.");
    } finally {
      this.saving.set(false);
    }
  }

  // ---------- Zoom and pan (the SVG viewBox is the camera) ----------

  viewBoxAttr = computed(() => { const v = this.view(); return `${v.x} ${v.y} ${v.w} ${v.h}`; });
  zoomed = computed(() => this.view().w < WIDTH - 1);

  private pointers = new Map<number, { x: number; y: number }>();
  private dragged = false;
  private downAt: { x: number; y: number } | null = null;
  private pinchStart: { dist: number; view: ViewBox } | null = null;

  resetView() { this.view.set({ x: 0, y: 0, w: WIDTH, h: HEIGHT }); }

  zoomBy(factor: number, center?: { x: number; y: number }) {
    const v = this.view();
    const w = Math.min(WIDTH, Math.max(WIDTH / MAX_ZOOM, v.w / factor));
    const h = w * HEIGHT / WIDTH;
    const cx = center?.x ?? v.x + v.w / 2;
    const cy = center?.y ?? v.y + v.h / 2;
    // Keep the point under the cursor in the same place
    const x = cx - (cx - v.x) * (w / v.w);
    const y = cy - (cy - v.y) * (h / v.h);
    this.view.set(this.clamp({ x, y, w, h }));
  }

  private zoomToBounds([[x0, y0], [x1, y1]]: [[number, number], [number, number]]) {
    const pad = 0.6;
    let w = Math.max((x1 - x0) * (1 + pad), (y1 - y0) * (1 + pad) * WIDTH / HEIGHT, WIDTH / MAX_ZOOM);
    w = Math.min(w, WIDTH);
    const h = w * HEIGHT / WIDTH;
    this.view.set(this.clamp({ x: (x0 + x1) / 2 - w / 2, y: (y0 + y1) / 2 - h / 2, w, h }));
  }

  private clamp(v: ViewBox): ViewBox {
    return { ...v, x: Math.min(Math.max(v.x, 0), WIDTH - v.w), y: Math.min(Math.max(v.y, 0), HEIGHT - v.h) };
  }

  /** Screen point -> map coordinates */
  private toMap(clientX: number, clientY: number) {
    const el = this.svg()!.nativeElement.getBoundingClientRect();
    const v = this.view();
    return { x: v.x + (clientX - el.left) * v.w / el.width, y: v.y + (clientY - el.top) * v.h / el.height };
  }

  onWheel(e: WheelEvent) {
    e.preventDefault();
    this.zoomBy(e.deltaY < 0 ? 1.25 : 0.8, this.toMap(e.clientX, e.clientY));
  }

  onPointerDown(e: PointerEvent) {
    this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    this.dragged = false;
    this.downAt = { x: e.clientX, y: e.clientY };
    if (this.pointers.size === 2) {
      const [a, b] = [...this.pointers.values()];
      this.pinchStart = { dist: Math.hypot(a.x - b.x, a.y - b.y), view: this.view() };
    }
  }

  onPointerMove(e: PointerEvent) {
    const prev = this.pointers.get(e.pointerId);
    if (!prev) return;
    this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

    if (this.pointers.size === 2 && this.pinchStart) {
      const [a, b] = [...this.pointers.values()];
      const dist = Math.hypot(a.x - b.x, a.y - b.y);
      const mid = this.toMap((a.x + b.x) / 2, (a.y + b.y) / 2);
      this.view.set(this.pinchStart.view);
      this.zoomBy(dist / this.pinchStart.dist, mid);
      this.dragged = true;
      return;
    }

    if (this.downAt && Math.hypot(e.clientX - this.downAt.x, e.clientY - this.downAt.y) > 5) this.dragged = true;
    if (!this.dragged || !this.zoomed()) return;
    const el = this.svg()!.nativeElement.getBoundingClientRect();
    const v = this.view();
    const dx = (e.clientX - prev.x) * v.w / el.width;
    const dy = (e.clientY - prev.y) * v.h / el.height;
    this.view.set(this.clamp({ ...v, x: v.x - dx, y: v.y - dy }));
  }

  onPointerUp(e: PointerEvent) {
    this.pointers.delete(e.pointerId);
    if (this.pointers.size < 2) this.pinchStart = null;
    // The click event fires after pointerup; clear the drag flag once it has had a chance to see it
    setTimeout(() => { if (this.pointers.size === 0) this.dragged = false; });
  }
}

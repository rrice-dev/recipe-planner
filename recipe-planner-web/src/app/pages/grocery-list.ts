import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { GroceryItem, Ingredient } from '../models';
import { formatQuantity, groupBySection } from '../store-sections';

@Component({
  selector: 'app-grocery-list',
  imports: [FormsModule],
  template: `
    <div class="page-header">
      <h1>Shopping list</h1>
      @if (items().length) {
        <div class="row no-print">
          <button type="button" (click)="copy()">{{ copied() ? 'Copied ✓' : 'Copy list' }}</button>
          @if (canNativeShare) {
            <button type="button" (click)="share()">Share…</button>
          }
          <button type="button" (click)="print()">Print</button>
        </div>
      }
    </div>

    <form class="card create no-print" (ngSubmit)="addTyped()">
      <div class="row">
        <input class="grow" name="newItem" [(ngModel)]="newName" placeholder="Add an item, e.g. eggs or paper towels"
               list="known-ingredients" maxlength="100" autocomplete="off" aria-label="Item to add" />
        <input class="narrow" name="newQty" type="number" min="0.01" step="any" [(ngModel)]="newQuantity" placeholder="qty" aria-label="Quantity" />
        <input class="narrow" name="newUnit" [(ngModel)]="newUnit" placeholder="unit" maxlength="20" aria-label="Unit" />
        <button type="submit" class="primary" [disabled]="!newName.trim() || busy()">Add</button>
      </div>
      <datalist id="known-ingredients">
        @for (i of known(); track i.id) {
          <option [value]="i.name"></option>
        }
      </datalist>
      <p class="muted small-text">Tip: use "Add to shopping list" on any recipe or meal plan to add everything at once.</p>
    </form>

    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    @if (loaded()) {
      @for (group of groups(); track group.section) {
        <section class="card shop-section">
          <h2>{{ group.section }}</h2>
          @for (item of group.items; track item.id) {
            <div class="shop-item" [class.checked]="item.checked">
              <input type="checkbox" [id]="'g' + item.id" [checked]="item.checked" (change)="toggle(item)" />
              <label [for]="'g' + item.id" class="grow">
                <span class="qty">{{ qty(item) }}</span>
                <span>{{ item.name }}</span>
                @if (item.sources) {
                  <span class="muted small-text sources">· {{ item.sources }}</span>
                }
              </label>
              <button type="button" class="link no-print" (click)="remove(item)" [attr.aria-label]="'Remove ' + item.name">×</button>
            </div>
          }
        </section>
      } @empty {
        <p class="muted">Your list is empty. Add items above, or from a recipe, a meal plan, or What Can I Make?</p>
      }

      @if (items().length) {
        <div class="row no-print">
          @if (checkedCount()) {
            <button type="button" (click)="clear(true)" [disabled]="busy()">Clear {{ checkedCount() }} checked</button>
          }
          <button type="button" class="link" (click)="clear(false)" [disabled]="busy()">Clear whole list</button>
        </div>
      }
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class GroceryListPage implements OnInit {
  private api = inject(ApiService);

  items = signal<GroceryItem[]>([]);
  known = signal<Ingredient[]>([]);
  loaded = signal(false);
  busy = signal(false);
  error = signal<string | null>(null);
  copied = signal(false);
  newName = '';
  newQuantity: number | null = null;
  newUnit = '';
  readonly canNativeShare = typeof navigator !== 'undefined' && 'share' in navigator;

  // Unchecked first within each aisle, so what's left to grab is always at the top
  groups = computed(() => groupBySection(this.items(),
    (a, b) => Number(a.checked) - Number(b.checked) || a.name.localeCompare(b.name)));
  checkedCount = computed(() => this.items().filter(i => i.checked).length);

  async ngOnInit() {
    await this.run(async () => {
      const [items, known] = await Promise.all([
        firstValueFrom(this.api.getGroceryList()),
        firstValueFrom(this.api.getIngredients())
      ]);
      this.items.set(items);
      this.known.set(known);
    });
    this.loaded.set(true);
  }

  qty(item: GroceryItem) {
    return [formatQuantity(item.quantity), item.unit === 'whole' ? '' : item.unit ?? ''].filter(Boolean).join(' ');
  }

  async addTyped() {
    const name = this.newName.trim();
    if (!name) return;
    await this.run(async () => {
      this.items.set(await firstValueFrom(this.api.addGroceryItems([{
        name,
        quantity: this.newQuantity && this.newQuantity > 0 ? this.newQuantity : null,
        unit: this.newUnit.trim() || null,
        source: null
      }])));
      this.newName = '';
      this.newQuantity = null;
      this.newUnit = '';
    });
  }

  async toggle(item: GroceryItem) {
    const checked = !item.checked;
    this.items.update(list => list.map(i => i.id === item.id ? { ...i, checked } : i)); // feels instant
    try {
      await firstValueFrom(this.api.updateGroceryItem(item.id, { checked }));
    } catch {
      this.items.update(list => list.map(i => i.id === item.id ? { ...i, checked: !checked } : i));
      this.error.set("Couldn't update that item. Please try again.");
    }
  }

  async remove(item: GroceryItem) {
    await this.run(async () => {
      await firstValueFrom(this.api.removeGroceryItem(item.id));
      this.items.update(list => list.filter(i => i.id !== item.id));
    });
  }

  async clear(checkedOnly: boolean) {
    if (!checkedOnly && !confirm('Clear your whole shopping list?')) return;
    await this.run(async () => {
      await firstValueFrom(this.api.clearGroceryList(checkedOnly));
      this.items.update(list => checkedOnly ? list.filter(i => !i.checked) : []);
    });
  }

  // Plain text that reads well in a text message or notes app
  asText() {
    const lines = ['Shopping list'];
    for (const group of this.groups()) {
      const open = group.items.filter(i => !i.checked);
      if (!open.length) continue;
      lines.push('', group.section);
      for (const i of open) lines.push(`- ${[this.qty(i), i.name].filter(Boolean).join(' ')}`);
    }
    return lines.join('\n');
  }

  async copy() {
    try {
      await navigator.clipboard.writeText(this.asText());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.error.set('Copying failed. Try Share or Print instead.');
    }
  }

  async share() {
    try {
      await navigator.share({ title: 'Shopping list', text: this.asText() });
    } catch {
      // Share sheet closed
    }
  }

  print() {
    window.print();
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch {
      this.error.set('Something went wrong. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}

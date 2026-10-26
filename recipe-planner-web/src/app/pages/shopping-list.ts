import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { ShoppingList, ShoppingListItem } from '../models';
import { pantryNote } from '../kitchen';

@Component({
  selector: 'app-shopping-list',
  imports: [RouterLink],
  template: `
    <a [routerLink]="['/meal-plans', id()]" class="back no-print">← Back to plan</a>
    @if (list(); as l) {
      <div class="page-header">
        <h1>Shopping list: {{ l.name }}</h1>
        <div class="row no-print">
          <button class="primary" (click)="addAll()" [disabled]="adding()">{{ adding() ? 'Adding…' : '🛒 Add all to my shopping list' }}</button>
          <button (click)="print()">Print</button>
        </div>
      </div>
      @if (note()) {
        <p class="muted small-text no-print">{{ note() }} <a routerLink="/shopping-list">View list</a></p>
      }

      @for (group of groups(); track group.section) {
        <section class="card shop-section">
          <h2>{{ group.section }}</h2>
          @for (item of group.items; track key(item)) {
            <label class="shop-item" [class.checked]="checked().has(key(item))">
              <input type="checkbox" [checked]="checked().has(key(item))" (change)="toggle(item)" />
              <span class="qty">{{ item.quantity }} {{ item.unit }}</span>
              <span>{{ item.ingredient }}</span>
            </label>
          }
        </section>
      } @empty {
        <p class="muted">Nothing to buy yet. Add recipes with ingredients to this plan.</p>
      }

      @if (inPantry().length) {
        <section class="card shop-section pantry-section">
          <h2>Already in your pantry</h2>
          <p class="small-text muted">Left off when you add this plan to your shopping list. <a routerLink="/kitchen" class="no-print">Edit pantry</a></p>
          @for (item of inPantry(); track key(item)) {
            <div class="shop-item in-pantry">
              <span class="qty">{{ item.quantity }} {{ item.unit }}</span>
              <span>{{ item.ingredient }}</span>
            </div>
          }
        </section>
      }
    } @else {
      <p class="muted">Loading…</p>
    }
  `
})
export class ShoppingListPage implements OnInit {
  private api = inject(ApiService);

  id = input.required<string>();
  list = signal<ShoppingList | null>(null);
  checked = signal(new Set<string>());
  adding = signal(false);
  note = signal<string | null>(null);

  async addAll() {
    this.adding.set(true);
    this.note.set(null);
    try {
      const result = await firstValueFrom(this.api.addMealPlanToGroceryList(Number(this.id())));
      this.note.set('Added everything from this plan.' + pantryNote(result.skippedPantry));
    } catch {
      this.note.set("Couldn't add to your list.");
    } finally {
      this.adding.set(false);
    }
  }

  inPantry = computed(() => this.list()?.items.filter(i => i.inPantry) ?? []);

  groups = computed(() => {
    const l = this.list();
    if (!l) return [];
    const map = new Map<string, ShoppingListItem[]>();
    for (const item of l.items.filter(i => !i.inPantry)) {
      const section = item.storeSection ?? 'Other';
      map.set(section, [...(map.get(section) ?? []), item]);
    }
    return [...map.entries()].map(([section, items]) => ({ section, items }));
  });

  async ngOnInit() {
    this.list.set(await firstValueFrom(this.api.getShoppingList(Number(this.id()))));
  }

  key(item: ShoppingListItem) {
    return `${item.ingredient}|${item.unit}`;
  }

  toggle(item: ShoppingListItem) {
    const k = this.key(item);
    this.checked.update(set => {
      const next = new Set(set);
      next.has(k) ? next.delete(k) : next.add(k);
      return next;
    });
  }

  print() {
    window.print();
  }
}
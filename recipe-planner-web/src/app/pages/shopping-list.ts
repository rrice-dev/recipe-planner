import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { ShoppingList, ShoppingListItem } from '../models';

@Component({
  selector: 'app-shopping-list',
  imports: [RouterLink],
  template: `
    <a [routerLink]="['/meal-plans', id()]" class="back no-print">← Back to plan</a>
    @if (list(); as l) {
      <div class="page-header">
        <h1>Shopping list: {{ l.name }}</h1>
        <button class="no-print" (click)="print()">Print</button>
      </div>

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

  groups = computed(() => {
    const l = this.list();
    if (!l) return [];
    const map = new Map<string, ShoppingListItem[]>();
    for (const item of l.items) {
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
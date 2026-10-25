import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { KrogerLocation, KrogerMatch, KrogerProductOption, KrogerStatus } from './models';

interface Row {
  match: KrogerMatch;
  include: boolean;
  upc: string | null;
  quantity: number;
}

@Component({
  selector: 'app-kroger-panel',
  imports: [FormsModule],
  template: `
    @if (status(); as s) {
      @if (s.available) {
        <section class="card kroger no-print">
          <div class="page-header">
            <h2>Send to Kroger</h2>
            @if (s.connected) {
              <button type="button" class="link" (click)="disconnect()">Disconnect Kroger</button>
            }
          </div>

          @if (notice()) {
            <p [class.error]="noticeIsError()" [class.list-note]="!noticeIsError()">{{ notice() }}</p>
          }

          @if (!s.connected) {
            <p class="small-text">Connect your Kroger account once, then send your list straight to your Kroger cart for pickup or delivery.
              You'll sign in on Kroger's own site; this app never sees your Kroger password.</p>
            <a class="button primary" href="/api/kroger/connect">Connect Kroger account</a>
          } @else if (!s.locationId || changingStore()) {
            <p class="small-text">Which Kroger do you shop at?</p>
            <form class="row" (ngSubmit)="findStores()">
              <input name="zip" [(ngModel)]="zip" placeholder="ZIP code" inputmode="numeric" maxlength="5" class="narrow" aria-label="ZIP code" />
              <button type="submit" [disabled]="busy()">Find stores</button>
            </form>
            @for (store of stores(); track store.locationId) {
              <label class="store-option">
                <input type="radio" name="store" (change)="pickStore(store)" />
                <span><strong>{{ store.name }}</strong> <span class="muted small-text">{{ store.address }}</span></span>
              </label>
            }
          } @else {
            <p class="small-text">Store: <strong>{{ s.locationName }}</strong>
              <button type="button" class="link" (click)="changingStore.set(true)">Change</button></p>

            @if (!rows().length) {
              <button type="button" class="primary" (click)="match()" [disabled]="busy() || !hasItems()">
                {{ busy() ? 'Finding products…' : 'Match my list to Kroger products' }}
              </button>
              @if (!hasItems()) {
                <p class="muted small-text">Add some items to your list first.</p>
              }
            } @else {
              <p class="muted small-text">Pick a product and how many packages for each item. Your choices are remembered for next time.</p>
              @for (row of rows(); track row.match.groceryItemId) {
                <div class="kroger-row" [class.skipped]="!row.include">
                  <input type="checkbox" [(ngModel)]="row.include" [name]="'inc' + row.match.groceryItemId"
                         [disabled]="!row.match.options.length" [attr.aria-label]="'Include ' + row.match.name" />
                  @if (image(row); as img) {
                    <img [src]="img" alt="" class="kroger-thumb" loading="lazy" referrerpolicy="no-referrer" />
                  } @else {
                    <span class="kroger-thumb"></span>
                  }
                  <div class="grow">
                    <div><strong>{{ row.match.name }}</strong>
                      @if (row.match.amount) { <span class="muted small-text note">· recipe needs {{ row.match.amount }}</span> }</div>
                    @if (row.match.options.length) {
                      <select [(ngModel)]="row.upc" [name]="'upc' + row.match.groceryItemId" [attr.aria-label]="'Product for ' + row.match.name">
                        @for (o of row.match.options; track o.upc) {
                          <option [ngValue]="o.upc">{{ label(o) }}</option>
                        }
                      </select>
                    } @else {
                      <span class="muted small-text">No match found at this store</span>
                    }
                  </div>
                  <label class="inline-label small-text">Qty
                    <input type="number" class="tiny" min="1" max="25" [(ngModel)]="row.quantity" [name]="'q' + row.match.groceryItemId" />
                  </label>
                </div>
              }
              <div class="row actions">
                <label class="inline-label"><input type="radio" name="modality" [(ngModel)]="modality" value="PICKUP" /> Pickup</label>
                <label class="inline-label"><input type="radio" name="modality" [(ngModel)]="modality" value="DELIVERY" /> Delivery</label>
                <button type="button" class="primary" (click)="send()" [disabled]="busy() || !selectedCount()">
                  {{ busy() ? 'Adding…' : 'Add ' + selectedCount() + ' to Kroger cart' }}
                </button>
                <button type="button" class="link" (click)="rows.set([])">Cancel</button>
              </div>
            }
            @if (sentCount()) {
              <p class="list-note">Added {{ sentCount() }} {{ sentCount() === 1 ? 'product' : 'products' }} to your Kroger cart and checked them off your list.
                <a href="https://www.kroger.com/cart" target="_blank" rel="noopener noreferrer">Open your Kroger cart</a> to pick a time and check out.</p>
            }
          }
        </section>
      }
    }
  `
})
export class KrogerPanel implements OnInit {
  private api = inject(ApiService);
  hasItems = input(false);
  krogerResult = input<string | null>(null);   // ?kroger=... after coming back from Kroger
  sent = output<void>();

  status = signal<KrogerStatus | null>(null);
  stores = signal<KrogerLocation[]>([]);
  rows = signal<Row[]>([]);
  busy = signal(false);
  changingStore = signal(false);
  notice = signal<string | null>(null);
  noticeIsError = signal(false);
  sentCount = signal(0);
  zip = '';
  modality: 'PICKUP' | 'DELIVERY' = 'PICKUP';

  // A plain method, not a computed signal: the checkboxes and dropdowns change the row objects
  // in place, which a signal can't see, so this has to be recalculated on every render
  selectedCount() {
    return this.rows().filter(r => r.include && r.upc).length;
  }

  async ngOnInit() {
    const result = this.krogerResult();
    if (result === 'connected') this.say('Kroger account connected.');
    else if (result === 'cancelled') this.say('Kroger connection was cancelled.', true);
    else if (result === 'error') this.say("Couldn't connect to Kroger. Please try again.", true);
    try {
      this.status.set(await firstValueFrom(this.api.getKrogerStatus()));
    } catch {
      this.status.set(null);   // panel just doesn't show
    }
  }

  async findStores() {
    if (!/^\d{5}$/.test(this.zip)) return this.say('Enter a 5-digit ZIP code.', true);
    await this.run(async () => {
      const found = await firstValueFrom(this.api.findKrogerStores(this.zip));
      this.stores.set(found);
      if (!found.length) this.say('No Kroger stores found near that ZIP.', true);
    });
  }

  async pickStore(store: KrogerLocation) {
    await this.run(async () => {
      await firstValueFrom(this.api.setKrogerStore(store.locationId, store.name));
      this.status.update(s => s && { ...s, locationId: store.locationId, locationName: store.name });
      this.changingStore.set(false);
      this.stores.set([]);
      this.rows.set([]);
    });
  }

  async match() {
    this.sentCount.set(0);
    await this.run(async () => {
      const matches = await firstValueFrom(this.api.matchKrogerProducts());
      this.rows.set(matches.map(m => ({ match: m, include: !!m.selectedUpc, upc: m.selectedUpc, quantity: 1 })));
      if (!matches.length) this.say('Nothing left to buy on your list.');
    });
  }

  async send() {
    const items = this.rows()
      .filter(r => r.include && r.upc)
      .map(r => ({
        groceryItemId: r.match.groceryItemId,
        upc: r.upc!,
        description: r.match.options.find(o => o.upc === r.upc)?.description ?? r.match.name,
        quantity: Math.min(25, Math.max(1, Math.round(r.quantity || 1)))
      }));
    await this.run(async () => {
      const { added } = await firstValueFrom(this.api.sendToKrogerCart(this.modality, items));
      this.rows.set([]);
      this.sentCount.set(added);
      this.sent.emit();
    });
  }

  async disconnect() {
    if (!confirm('Disconnect your Kroger account? You can connect again any time.')) return;
    await this.run(async () => {
      await firstValueFrom(this.api.disconnectKroger());
      this.status.update(s => s && { ...s, connected: false, locationId: null, locationName: null });
      this.rows.set([]);
      this.say('Kroger disconnected.');
    });
  }

  label(o: KrogerProductOption) {
    const price = o.promoPrice ?? o.price;
    return [o.description, o.size, price != null ? `$${price.toFixed(2)}` : null].filter(Boolean).join(' · ');
  }

  image(row: Row) {
    return row.match.options.find(o => o.upc === row.upc)?.imageUrl ?? null;
  }

  private say(message: string, isError = false) {
    this.notice.set(message);
    this.noticeIsError.set(isError);
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.notice.set(null);
    try {
      await action();
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      if (status === 409 && e instanceof HttpErrorResponse && e.error?.title === 'kroger_not_connected') {
        this.status.update(s => s && { ...s, connected: false });
        this.say('Your Kroger connection expired. Please connect again.', true);
      } else {
        this.say(
          status === 429 ? "You've made a lot of Kroger requests this hour. Try again later." :
          status === 502 ? "Kroger isn't responding right now. Try again in a minute." :
          status === 400 ? 'Something in that request was off. Check your choices and try again.' :
          'Something went wrong talking to Kroger. Please try again.', true);
      }
    } finally {
      this.busy.set(false);
    }
  }
}

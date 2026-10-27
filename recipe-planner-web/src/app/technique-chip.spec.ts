import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TechniqueChip } from './technique-chip';

describe('TechniqueChip', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    // jsdom doesn't implement <dialog> modals, so stand in for them
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) { this.removeAttribute('open'); };
    await TestBed.configureTestingModule({
      imports: [TechniqueChip],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function chip(name: string) {
    const fixture = TestBed.createComponent(TechniqueChip);
    fixture.componentRef.setInput('name', name);
    fixture.detectChanges();
    return fixture;
  }

  it('opens the details for its technique, and looks the list up only once', async () => {
    const braise = chip('braise');   // matched regardless of case
    braise.nativeElement.querySelector('button.technique-chip').click();
    http.expectOne('/api/techniques').flush([
      { id: 1, name: 'Braise', summary: 'Brown, then cook low and slow in a little liquid.', donenessCues: 'Fork-tender', commonMistakes: null, difficulty: 3 },
      { id: 2, name: 'Sear', summary: 'High heat for a crust.', donenessCues: null, commonMistakes: 'Crowding the pan', difficulty: 2 },
    ]);
    await braise.whenStable();
    braise.detectChanges();
    const dialog: HTMLDialogElement = braise.nativeElement.querySelector('dialog');
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(dialog.textContent).toContain('Braise');
    expect(dialog.textContent).toContain('low and slow');
    expect(dialog.textContent).toContain('Fork-tender');

    braise.nativeElement.querySelector('button.close').click();
    expect(dialog.hasAttribute('open')).toBe(false);

    // A second chip uses the list already loaded: no new request (afterEach checks)
    const unknown = chip('Sous vide');
    unknown.nativeElement.querySelector('button.technique-chip').click();
    await unknown.whenStable();
    unknown.detectChanges();
    expect(unknown.nativeElement.querySelector('dialog').textContent).toContain("Couldn't find details");
  });
});

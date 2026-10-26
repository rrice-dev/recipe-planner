import { TestBed } from '@angular/core/testing';
import { ServingsStepper } from './servings-stepper';

describe('ServingsStepper', () => {
  function create(value: number, base: number) {
    const fixture = TestBed.createComponent(ServingsStepper);
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('base', base);
    fixture.detectChanges();
    const emitted: number[] = [];
    fixture.componentInstance.valueChange.subscribe(v => emitted.push(v));
    const el = fixture.nativeElement as HTMLElement;
    const button = (label: string) => el.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
    return { fixture, el, emitted, button };
  }

  it('steps up and down by one', () => {
    const { emitted, button } = create(4, 4);
    button('More servings').click();
    button('Fewer servings').click();
    expect(emitted).toEqual([5, 3]);
  });

  it("can't go below 1 or above 100", () => {
    expect(create(1, 4).button('Fewer servings').disabled).toBe(true);
    expect(create(100, 4).button('More servings').disabled).toBe(true);
  });

  it('clamps typed values', () => {
    const { el, emitted } = create(4, 4);
    const input = el.querySelector('input') as HTMLInputElement;
    input.value = '500';
    input.dispatchEvent(new Event('change'));
    input.value = '0';
    input.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([100, 1]);
  });

  it('offers a reset only after a change', () => {
    expect(create(4, 4).el.textContent).not.toContain('Reset');
    const { el, emitted } = create(8, 4);
    expect(el.textContent).toContain('Recipe makes 4');
    (el.querySelector('button.link') as HTMLButtonElement).click();
    expect(emitted).toEqual([4]);
  });
});

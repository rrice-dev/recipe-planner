import { applyTheme } from './settings.service';

describe('applyTheme', () => {
  afterEach(() => { document.documentElement.removeAttribute('data-theme'); localStorage.clear(); });

  it('sets light or dark on the page and remembers it in this browser', () => {
    applyTheme('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('rp-theme')).toBe('dark');
    applyTheme('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('follows the device for "system"', () => {
    applyTheme('dark');
    applyTheme('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem('rp-theme')).toBeNull();
  });
});

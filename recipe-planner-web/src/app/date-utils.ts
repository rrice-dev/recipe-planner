export function parseDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function nextMonday(from = new Date()): string {
  const d = new Date(from.getFullYear(), from.getMonth(), from.getDate());
  const diff = (1 - d.getDay() + 7) % 7;
  d.setDate(d.getDate() + diff);
  return toIsoDate(d);
}

export function weekDays(weekStart: string): { iso: string; label: string }[] {
  const start = parseDate(weekStart);
  return Array.from({ length: 7 }, (_, i) => {
    const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
    return {
      iso: toIsoDate(d),
      label: d.toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' })
    };
  });
}

export function formatWeek(weekStart: string): string {
  return 'Week of ' + parseDate(weekStart).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
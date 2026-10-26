// Finds cooking times in a step's text, so cook mode can offer a timer for each.

export interface FoundTime {
  /** As written, e.g. "6–8 minutes" */
  label: string;
  /** Seconds until the first check: the low end of a range, since it's easier to cook longer than to un-cook */
  seconds: number;
}

const UNIT_SECONDS: Record<string, number> = { sec: 1, second: 1, min: 60, minute: 60, hr: 3600, hour: 3600 };

// "10 minutes", "6–8 min", "1.5 to 2 hours", "30 seconds", "1 hour"
const TIME = /(\d+(?:\.\d+)?)(?:\s*(?:–|-|to)\s*(\d+(?:\.\d+)?))?\s*(seconds?|secs?|minutes?|mins?|hours?|hrs?)\b/gi;

export function findTimes(text: string): FoundTime[] {
  const found: FoundTime[] = [];
  for (const m of text.matchAll(TIME)) {
    const unit = m[3].toLowerCase().replace(/s$/, '');
    const perUnit = UNIT_SECONDS[unit];
    if (!perUnit) continue;
    const seconds = Math.round(parseFloat(m[1]) * perUnit);
    // Ignore zero, and anything over 24 hours (that's not a kitchen timer, that's a calendar)
    if (seconds <= 0 || seconds > 86400) continue;
    if (!found.some(f => f.seconds === seconds)) found.push({ label: m[0], seconds });
  }
  return found;
}

/** 75 -> "1:15", 3725 -> "1:02:05" */
export function formatClock(totalSeconds: number): string {
  const s = Math.max(0, Math.ceil(totalSeconds));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return h ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
}

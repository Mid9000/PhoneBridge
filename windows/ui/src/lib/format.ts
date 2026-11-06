import { t } from '../i18n';

const time = new Intl.DateTimeFormat(t.locale, { hour: '2-digit', minute: '2-digit' });
const weekday = new Intl.DateTimeFormat(t.locale, { weekday: 'long' });
const short = new Intl.DateTimeFormat(t.locale, { day: 'numeric', month: 'short' });
const long = new Intl.DateTimeFormat(t.locale, { day: 'numeric', month: 'long', year: 'numeric' });
const DAY = 86_400_000;

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();

/** "à l'instant", "il y a 5 min", "il y a 3 h" (up to 12 h), then a date. */
export function ago(iso: string | null, now = Date.now()): string {
  if (!iso) return t.time.never;
  const min = Math.round((now - Date.parse(iso)) / 60_000);
  if (min < 1) return t.time.now;
  if (min < 60) return t.time.minutes(min);
  if (min < 12 * 60) return t.time.hours(Math.round(min / 60));
  return stamp(iso, now);
}

/** List timestamp like macOS Messages: "14:32", "Hier", "lundi", "12 mars". */
export function stamp(iso: string, now = Date.now()): string {
  const d = new Date(iso);
  const days = Math.round((startOfDay(new Date(now)) - startOfDay(d)) / DAY);
  if (days <= 0) return time.format(d);
  if (days === 1) return t.time.yesterday;
  if (days < 7) return weekday.format(d);
  return short.format(d);
}

/** Thread separator: "Aujourd'hui 14:32", "Hier 09:10", "lundi 18:00". */
export function daySeparator(iso: string, now = Date.now()): string {
  const d = new Date(iso);
  const days = Math.round((startOfDay(new Date(now)) - startOfDay(d)) / DAY);
  const label = days <= 0 ? t.time.today : days === 1 ? t.time.yesterday : days < 7 ? weekday.format(d) : short.format(d);
  return `${label} ${time.format(d)}`;
}

export const longDate = (iso: string) => long.format(new Date(iso));

export function bytes(n: number): string {
  let i = 0;
  while (n >= 1024 && i < t.bytes.length - 1) { n /= 1024; i++; }
  return `${new Intl.NumberFormat(t.locale, { maximumFractionDigits: i === 0 || n >= 100 ? 0 : 1 }).format(n)} ${t.bytes[i]}`;
}

export function initials(name: string): string {
  const words = name.replace(/[^\p{L}\s'-]/gu, '').trim().split(/\s+/).filter(Boolean);
  return words.slice(0, 2).map((w) => w[0]!.toUpperCase()).join('');
}

/** Stable hue (0-359) from a string, for avatars and app tiles. */
export function hue(seed: string): number {
  let h = 0;
  for (const ch of seed) h = (h * 31 + ch.codePointAt(0)!) >>> 0;
  return h % 360;
}

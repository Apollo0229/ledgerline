const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const whole = new Intl.NumberFormat("en-US");
const day = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeZone: "UTC" });
const monthYear = new Intl.DateTimeFormat("en-US", { month: "short", year: "numeric", timeZone: "UTC" });

export const money = (n: number) => usd.format(n);

/**
 * 607.8M, 7.9B, 20K. Written out by hand: Intl's compact notation depends on the ICU data bundled with
 * the runtime, and Node on Linux and on Windows disagree ("$69M" vs "$69.0M").
 */
function compact(n: number, decimals: number): string {
  const [div, unit] = n >= 1e9 ? [1e9, "B"] : n >= 1e6 ? [1e6, "M"] : n >= 1e3 ? [1e3, "K"] : [1, ""];
  return `${(n / div).toFixed(div === 1 ? 0 : decimals)}${unit}`;
}

/** $69.0M; exact under $10,000 where compact notation would hide the cents that matter. */
export const moneyCompact = (n: number) =>
  Math.abs(n) < 10_000 ? usd.format(n) : `${n < 0 ? "-" : ""}$${compact(Math.abs(n), 1)}`;

export const count = (n: number) => whole.format(n);

/** For axis ticks: in full under 10,000, compact above (10K, 20K). Ticks are round, so no decimals. */
export const tick = (n: number) => (n < 10_000 ? whole.format(n) : compact(n, 0));

/** "2026-11-06" → "Nov 6, 2026". Dates are calendar days, so they are read as UTC to avoid shifting. */
export const date = (iso: string) => day.format(new Date(`${iso}T00:00:00Z`));

export const shortMonth = (iso: string) => monthYear.format(new Date(`${iso}T00:00:00Z`));

export const duration = (ms: number) => (ms < 1_000 ? `${Math.round(ms)} ms` : `${(ms / 1_000).toFixed(1)} s`);

/** Readable name for a segment: the hot tier is the newest months, the rest are calendar years. */
export const segmentName = (label: string) => (label === "hot" ? "Last 3 months" : label);

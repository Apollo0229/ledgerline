const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const usdCompact = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  notation: "compact",
  maximumFractionDigits: 1,
});
const whole = new Intl.NumberFormat("en-US");
const wholeCompact = new Intl.NumberFormat("en-US", { notation: "compact", maximumFractionDigits: 1 });
const day = new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeZone: "UTC" });
const monthYear = new Intl.DateTimeFormat("en-US", { month: "short", year: "numeric", timeZone: "UTC" });

export const money = (n: number) => usd.format(n);

/** $69.0M; exact under $10,000 where compact notation would hide the cents that matter. */
export const moneyCompact = (n: number) => (Math.abs(n) < 10_000 ? usd.format(n) : usdCompact.format(n));

export const count = (n: number) => whole.format(n);

/** For axis ticks: in full under 10,000, compact above (10K, 20K). */
export const tick = (n: number) => (n < 10_000 ? whole.format(n) : wholeCompact.format(n));

/** "2026-11-06" → "Nov 6, 2026". Dates are calendar days, so they are read as UTC to avoid shifting. */
export const date = (iso: string) => day.format(new Date(`${iso}T00:00:00Z`));

export const shortMonth = (iso: string) => monthYear.format(new Date(`${iso}T00:00:00Z`));

export const duration = (ms: number) => (ms < 1_000 ? `${Math.round(ms)} ms` : `${(ms / 1_000).toFixed(1)} s`);

/** Readable name for a segment: the hot tier is the newest months, the rest are calendar years. */
export const segmentName = (label: string) => (label === "hot" ? "Last 3 months" : label);

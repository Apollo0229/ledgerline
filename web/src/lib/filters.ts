import { STATUSES, type Status } from "./types.ts";

export type Range = "90d" | "1y" | "3y" | "all" | "custom";

export const RANGES: { value: Range; label: string }[] = [
  { value: "90d", label: "Last 90 days" },
  { value: "1y", label: "Last 12 months" },
  { value: "3y", label: "Last 3 years" },
  { value: "all", label: "All 7 years" },
  { value: "custom", label: "Custom range" },
];

/** What the filter bar holds. Strings, as typed; the API validates the same rules again. */
export interface Filters {
  range: Range;
  from: string;
  to: string;
  text: string;
  client: string;
  status: Status | "";
  min: string;
  max: string;
}

export const EMPTY: Filters = { range: "all", from: "", to: "", text: "", client: "", status: "", min: "", max: "" };

/** Read filters from a URL query string, so a search can be bookmarked and shared. */
export function fromSearch(search: string): Filters {
  const q = new URLSearchParams(search);
  const range = q.get("range");
  const status = q.get("status");
  return {
    range: RANGES.some((r) => r.value === range) ? (range as Range) : EMPTY.range,
    from: q.get("from") ?? "",
    to: q.get("to") ?? "",
    text: q.get("text") ?? "",
    client: q.get("client") ?? "",
    status: STATUSES.includes(status as Status) ? (status as Status) : "",
    min: q.get("min") ?? "",
    max: q.get("max") ?? "",
  };
}

/** The address-bar form: only what differs from the defaults. */
export function toSearch(f: Filters): string {
  const q = new URLSearchParams();
  if (f.range !== EMPTY.range) q.set("range", f.range);
  if (f.range === "custom") {
    if (f.from) q.set("from", f.from);
    if (f.to) q.set("to", f.to);
  }
  for (const key of ["text", "client", "status", "min", "max"] as const) {
    const v = f[key].trim();
    if (v) q.set(key, v);
  }
  return q.toString();
}

/** The API query: presets become a concrete `from` date relative to `today`. */
export function toApiQuery(f: Filters, today: Date): string {
  const q = new URLSearchParams();
  const days = { "90d": 90, "1y": 365, "3y": 3 * 365 }[f.range as string];
  if (days !== undefined) q.set("from", isoDay(addDays(today, -days)));
  if (f.range === "custom") {
    if (f.from) q.set("from", f.from);
    if (f.to) q.set("to", f.to);
  }
  for (const key of ["text", "client", "status", "min", "max"] as const) {
    const v = f[key].trim();
    if (v) q.set(key, v);
  }
  return q.toString();
}

/** The first problem with the filters, or null. Checked before searching, mirroring the API. */
export function problem(f: Filters): string | null {
  const min = f.min.trim() === "" ? null : Number(f.min);
  const max = f.max.trim() === "" ? null : Number(f.max);
  if ((min !== null && !Number.isFinite(min)) || (max !== null && !Number.isFinite(max))) return "Amounts must be numbers.";
  if (min !== null && min < 0) return "The minimum amount cannot be negative.";
  if (min !== null && max !== null && min > max) return "The maximum amount must be at least the minimum.";
  if (f.range === "custom" && f.from && f.to && f.from > f.to) return "The end date must be on or after the start date.";
  if (f.text.length > 200) return "Search text is limited to 200 characters.";
  return null;
}

const addDays = (d: Date, n: number) => new Date(d.getTime() + n * 86_400_000);
const isoDay = (d: Date) => d.toISOString().slice(0, 10);

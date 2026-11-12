import type { Filters } from "./filters.ts";
import type { Action } from "./stream.ts";
import type { DoneEvent, PageEvent, PlanEvent, ProgressEvent } from "./types.ts";

/** Built with VITE_DEMO=1, the portal replays searches recorded from the real stack instead of calling an API. */
export const DEMO = import.meta.env.VITE_DEMO === "1";

/** Written by `Ledgerline.Bench record`: real streamed searches with the times their events arrived. */
export interface Recording {
  recorded_at: string;
  note: string;
  environment: { payments: number; indices: number; elasticsearch_version: string; logical_cpus: number };
  clients: string[];
  searches: RecordedSearch[];
}

export interface RecordedSearch {
  id: string;
  label: string;
  note: string | null;
  filters: Partial<Filters>;
  query: string;
  /** The fair baseline: one request with the same page, totals and per-year breakdown. */
  baseline_ms: number;
  first_page_ms: number;
  done_ms: number;
  matches: number;
  events: RecordedEvent[];
}

export type RecordedEvent =
  | { t: number; type: "plan"; data: PlanEvent }
  | { t: number; type: "page"; data: PageEvent }
  | { t: number; type: "progress"; data: ProgressEvent }
  | { t: number; type: "done"; data: DoneEvent };

/**
 * The reducer action for a recorded event. Timings are always reported at the recorded speed, so a
 * slow-motion replay shows what the stack really did, not how long the replay took.
 */
export function toAction(e: RecordedEvent, startedAt: number): Action {
  const now = startedAt + e.t;
  switch (e.type) {
    case "plan":
      return { type: "plan", event: e.data };
    case "page":
      return { type: "page", event: e.data, now };
    case "progress":
      return { type: "progress", event: e.data };
    case "done":
      return { type: "done", event: e.data, now };
  }
}

/** When each event is shown, in milliseconds after the replay starts, at `slowdown` times slower than real. */
export const schedule = (events: RecordedEvent[], slowdown: number) => events.map((e) => ({ at: e.t * slowdown, e }));

/** "Client: Harborview Health System · Min: $5,000", for showing which filters a recording used. */
export function describeFilters(f: Partial<Filters>): string[] {
  const parts: string[] = [];
  if (f.range === "custom") parts.push(`Deposited ${f.from ?? "…"} to ${f.to ?? "…"}`);
  else parts.push("All 7 years");
  if (f.text) parts.push(`Text “${f.text}”`);
  if (f.client) parts.push(`Client ${f.client}`);
  if (f.status) parts.push(`Status ${f.status}`);
  if (f.min) parts.push(`Min $${Number(f.min).toLocaleString("en-US")}`);
  if (f.max) parts.push(`Max $${Number(f.max).toLocaleString("en-US")}`);
  return parts;
}

import type { DoneEvent, PageEvent, Payment, PlanEvent, ProgressEvent, Totals } from "./types.ts";

export interface Segment {
  label: string;
  from: string;
  to: string;
  /** Undefined until this segment has been searched. */
  totals?: Totals;
}

/** Everything the page shows about one search, built up event by event. */
export interface SearchState {
  phase: "idle" | "searching" | "done" | "error";
  /** Client clock (performance.now) when the search started; timings below are measured from it. */
  startedAt: number;
  segments: Segment[];
  hits: Payment[];
  /** True once some page event has arrived for this search. */
  hasPage: boolean;
  /** True when no later segment can change the page. */
  pageComplete: boolean;
  running: Totals;
  /** Time to the first page event as the person experienced it, network included. It may still grow. */
  firstPageMs?: number;
  /** Time to the page being exact: no later segment can change it. Often the same moment as the first page. */
  exactPageMs?: number | undefined;
  doneMs?: number;
  error?: string;
}

export type Action =
  | { type: "start"; now: number }
  | { type: "plan"; event: PlanEvent }
  | { type: "page"; event: PageEvent; now: number }
  | { type: "progress"; event: ProgressEvent }
  | { type: "done"; event: DoneEvent; now: number }
  | { type: "error"; message: string };

export const EMPTY_TOTALS: Totals = { count: 0, amount: 0, by_status: {} };

export const initial: SearchState = {
  phase: "idle",
  startedAt: 0,
  segments: [],
  hits: [],
  hasPage: false,
  pageComplete: false,
  running: EMPTY_TOTALS,
};

/**
 * A new search keeps the previous hits and segments on screen (dimmed by the page) until its own
 * arrive, so refetching never flashes empty. Everything else resets.
 */
export function reduce(state: SearchState, action: Action): SearchState {
  switch (action.type) {
    case "start":
      return {
        ...initial,
        phase: "searching",
        startedAt: action.now,
        hits: state.hits,
        segments: state.segments,
      };
    case "plan":
      return { ...state, segments: action.event.segments.map((s) => ({ ...s })) };
    case "page":
      return {
        ...state,
        hits: action.event.hits,
        hasPage: true,
        pageComplete: action.event.complete,
        firstPageMs: state.firstPageMs ?? action.now - state.startedAt,
        exactPageMs: state.exactPageMs ?? (action.event.complete ? action.now - state.startedAt : undefined),
      };
    case "progress":
      return {
        ...state,
        running: action.event.running,
        segments: state.segments.map((s) =>
          s.label === action.event.segment ? { ...s, totals: action.event.segment_totals } : s,
        ),
      };
    case "done":
      return {
        ...state,
        phase: "done",
        running: action.event.totals,
        doneMs: action.now - state.startedAt,
        exactPageMs: state.exactPageMs ?? action.now - state.startedAt,
        // A search with no matches anywhere still ends with a (complete, empty) page.
        hasPage: true,
        pageComplete: true,
      };
    case "error":
      return { ...state, phase: "error", error: action.message };
  }
}

/** How many planned segments have reported. */
export const searched = (s: SearchState) => s.segments.filter((x) => x.totals).length;

/** The page and totals belong to an earlier search and are only kept on screen to avoid a flash. */
export const isStale = (s: SearchState) => s.phase === "searching" && !s.hasPage;

import { describe, expect, it } from "vitest";
import { EMPTY, fromSearch, problem, toApiQuery, toSearch } from "../src/lib/filters.ts";
import { count, date, duration, moneyCompact, tick } from "../src/lib/format.ts";
import { initial, isStale, reduce, searched, type SearchState } from "../src/lib/stream.ts";
import type { Payment, Totals } from "../src/lib/types.ts";

const totals = (n: number): Totals => ({ count: n, amount: n * 10, by_status: { posted: n } });
const hit = (id: string): Payment => ({
  id,
  deposit_date: "2026-11-06",
  client: "c",
  lockbox: "LB-1000",
  payer: "p",
  check_number: "1",
  amount: 1,
  invoices: [],
  status: "posted",
  memo: "",
});

const plan = {
  segments: [
    { label: "hot", from: "2026-09-01", to: "2026-11-06" },
    { label: "2026", from: "2026-01-01", to: "2026-08-31" },
  ],
  at_ms: 0,
};

const progress = (segment: string, own: number, running: number, n: number) => ({
  segment,
  from: "",
  to: "",
  segment_totals: totals(own),
  running: totals(running),
  searched: n,
  segments: 2,
  at_ms: 0,
});

describe("stream reducer", () => {
  it("builds a search up event by event, timing the first page on the client clock", () => {
    let s: SearchState = reduce(initial, { type: "start", now: 1_000 });
    s = reduce(s, { type: "plan", event: plan });
    expect(s.segments.map((x) => x.totals)).toEqual([undefined, undefined]);

    s = reduce(s, { type: "page", event: { hits: [hit("a")], complete: true, at_ms: 12 }, now: 1_040 });
    s = reduce(s, { type: "progress", event: progress("hot", 3, 3, 1) });
    expect(s.firstPageMs).toBe(40);
    expect(searched(s)).toBe(1);

    s = reduce(s, { type: "progress", event: progress("2026", 4, 7, 2) });
    s = reduce(s, { type: "done", event: { totals: totals(7), first_page_ms: 12, at_ms: 30 }, now: 1_090 });
    expect(s).toMatchObject({ phase: "done", doneMs: 90, pageComplete: true, running: totals(7) });
    expect(s.segments.map((x) => x.totals?.count)).toEqual([3, 4]);
  });

  it("keeps only the first page time when the page grows", () => {
    let s = reduce(initial, { type: "start", now: 0 });
    s = reduce(s, { type: "page", event: { hits: [hit("a")], complete: false, at_ms: 5 }, now: 10 });
    s = reduce(s, { type: "page", event: { hits: [hit("a"), hit("b")], complete: true, at_ms: 9 }, now: 25 });
    expect(s.firstPageMs).toBe(10);
    expect(s.hits.map((h) => h.id)).toEqual(["a", "b"]);
  });

  it("keeps the previous results on screen, marked stale, until the new search has a page", () => {
    let s = reduce(initial, { type: "start", now: 0 });
    s = reduce(s, { type: "page", event: { hits: [hit("old")], complete: true, at_ms: 1 }, now: 1 });
    s = reduce(s, { type: "done", event: { totals: totals(1), first_page_ms: 1, at_ms: 2 }, now: 2 });

    s = reduce(s, { type: "start", now: 100 });
    expect(s.hits.map((h) => h.id)).toEqual(["old"]);
    expect(isStale(s)).toBe(true);
    expect(s.running.count).toBe(0);

    s = reduce(s, { type: "page", event: { hits: [hit("new")], complete: true, at_ms: 1 }, now: 105 });
    expect(isStale(s)).toBe(false);
  });

  it("a search with no matches anywhere ends with an empty, complete page", () => {
    let s = reduce(initial, { type: "start", now: 0 });
    s = reduce(s, { type: "done", event: { totals: totals(0), first_page_ms: null, at_ms: 3 }, now: 4 });
    expect(s).toMatchObject({ hasPage: true, pageComplete: true, hits: [] });
  });

  it("records an error without losing what had arrived", () => {
    let s = reduce(initial, { type: "start", now: 0 });
    s = reduce(s, { type: "page", event: { hits: [hit("a")], complete: false, at_ms: 1 }, now: 1 });
    s = reduce(s, { type: "error", message: "boom" });
    expect(s).toMatchObject({ phase: "error", error: "boom" });
    expect(s.hits).toHaveLength(1);
  });
});

describe("filters", () => {
  it("round-trips through the address bar, leaving out defaults", () => {
    const f = { ...EMPTY, text: "acme ", status: "exception" as const, min: "5000" };
    expect(toSearch(f)).toBe("text=acme&status=exception&min=5000");
    expect(fromSearch(toSearch(f))).toEqual({ ...f, text: "acme" });
    expect(toSearch(EMPTY)).toBe("");
  });

  it("ignores unknown values in a shared link", () => {
    expect(fromSearch("?range=forever&status=bounced")).toEqual(EMPTY);
  });

  it("turns a preset into a concrete from date for the API", () => {
    const today = new Date("2026-11-08T15:00:00Z");
    expect(toApiQuery({ ...EMPTY, range: "90d" }, today)).toBe("from=2026-08-10");
    expect(toApiQuery({ ...EMPTY, range: "custom", from: "2021-01-01", to: "2021-12-31" }, today)).toBe(
      "from=2021-01-01&to=2021-12-31",
    );
    expect(toApiQuery({ ...EMPTY, range: "all", from: "2021-01-01" }, today)).toBe("");
  });

  it("catches what the API would reject, before searching", () => {
    expect(problem(EMPTY)).toBeNull();
    expect(problem({ ...EMPTY, min: "10", max: "5" })).toMatch(/maximum/);
    expect(problem({ ...EMPTY, min: "-1" })).toMatch(/negative/);
    expect(problem({ ...EMPTY, max: "lots" })).toMatch(/numbers/);
    expect(problem({ ...EMPTY, range: "custom", from: "2025-02-01", to: "2025-01-01" })).toMatch(/end date/);
  });
});

describe("format", () => {
  it("keeps cents under $10,000 and compacts above", () => {
    expect(moneyCompact(1_234.5)).toBe("$1,234.50");
    expect(moneyCompact(69_027_222.04)).toBe("$69.0M");
    expect(moneyCompact(607_812_345)).toBe("$607.8M");
    expect(moneyCompact(7_872_666_649.2)).toBe("$7.9B");
    expect(moneyCompact(48_200)).toBe("$48.2K");
  });

  it("formats counts, ticks, dates and durations", () => {
    expect(count(9_368)).toBe("9,368");
    expect(tick(5_000)).toBe("5,000");
    expect(tick(20_000)).toBe("20K");
    expect(tick(300_000)).toBe("300K");
    expect(tick(2_000_000)).toBe("2M");
    expect(date("2026-11-06")).toBe("Nov 6, 2026");
    expect(duration(15.4)).toBe("15 ms");
    expect(duration(1_250)).toBe("1.3 s");
  });
});

describe("chart ticks", async () => {
  const { niceStep } = await import("../src/components/YearChart.tsx");

  it("steps by 1, 2 or 5 times a power of ten, at most four steps to the top", () => {
    expect(niceStep(1_649)).toBe(500);
    expect(niceStep(9_368)).toBe(5_000);
    expect(niceStep(7)).toBe(2);
    expect(niceStep(0)).toBe(1);
    for (const max of [3, 48, 999, 1_649, 31_582, 412_000]) expect(max / niceStep(max)).toBeLessThanOrEqual(4);
  });
});

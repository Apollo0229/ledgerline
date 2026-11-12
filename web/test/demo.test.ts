import { describe, expect, it } from "vitest";
import { describeFilters, type RecordedEvent, schedule, toAction } from "../src/lib/demo.ts";
import { initial, reduce } from "../src/lib/stream.ts";

const totals = (n: number) => ({ count: n, amount: n, by_status: { posted: n } });

const events: RecordedEvent[] = [
  { t: 2.1, type: "plan", data: { segments: [{ label: "hot", from: "2026-09-01", to: "2026-11-11" }], at_ms: 0 } },
  { t: 41.0, type: "page", data: { hits: [], complete: true, at_ms: 39 } },
  {
    t: 41.2,
    type: "progress",
    data: { segment: "hot", from: "", to: "", segment_totals: totals(5), running: totals(5), searched: 1, segments: 1, at_ms: 39 },
  },
  { t: 161.9, type: "done", data: { totals: totals(5), first_page_ms: 39, at_ms: 160 } },
];

describe("demo replay", () => {
  it("reports the recorded timings, however slowly it is replayed", () => {
    let s = reduce(initial, { type: "start", now: 0 });
    for (const { e } of schedule(events, 10)) s = reduce(s, toAction(e, 0));

    expect(s.firstPageMs).toBe(41.0);
    expect(s.doneMs).toBe(161.9);
    expect(s).toMatchObject({ phase: "done", pageComplete: true, running: totals(5) });
    expect(s.segments.map((x) => x.totals?.count)).toEqual([5]);
  });

  it("stretches the schedule by the slowdown, keeping the order", () => {
    expect(schedule(events, 10).map((x) => x.at)).toEqual([21, 410, 412, 1619]);
    expect(schedule(events, 1).map((x) => x.e.type)).toEqual(["plan", "page", "progress", "done"]);
  });

  it("describes the filters a recording used", () => {
    expect(describeFilters({ range: "all", client: "Harborview Health System", min: "5000" })).toEqual([
      "All 7 years",
      "Client Harborview Health System",
      "Min $5,000",
    ]);
    expect(describeFilters({ range: "custom", from: "2021-01-01", to: "2021-12-31" })).toEqual([
      "Deposited 2021-01-01 to 2021-12-31",
    ]);
  });
});

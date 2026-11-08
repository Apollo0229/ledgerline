import { useState } from "react";
import { count, moneyCompact, segmentName, shortMonth, tick } from "../lib/format.ts";
import { isStale, type Segment, type SearchState } from "../lib/stream.ts";

const PLOT_HEIGHT = 160;

/** Round tick step (1, 2 or 5 × 10ⁿ) giving at most four steps up to `max`. */
export function niceStep(max: number): number {
  if (max <= 0) return 1;
  const raw = max / 4;
  const pow = 10 ** Math.floor(Math.log10(raw));
  return [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw) ?? 10 * pow;
}

/**
 * Matches per segment, oldest on the left and the hot tier on the right. It fills in right to left
 * as the stream reports newest first; segments not searched yet show as pending at the baseline.
 * One series, so no legend: the title says what is plotted. Every value is also in the table view.
 */
export function YearChart({ state }: { state: SearchState }) {
  const [table, setTable] = useState(false);
  const [active, setActive] = useState<string | null>(null);
  const columns = [...state.segments].reverse();
  const max = Math.max(0, ...columns.map((c) => c.totals?.count ?? 0));
  const step = niceStep(max);
  const top = Math.max(step, Math.ceil(max / step) * step);
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);

  return (
    <section
      aria-labelledby="by-year"
      className={`rounded-xl border border-line bg-surface p-4 transition-opacity ${isStale(state) ? "opacity-50" : ""}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 id="by-year" className="font-semibold">
            Matches by year
          </h2>
          <p className="text-sm text-ink-2">Newest first: the right-hand column arrives first</p>
        </div>
        <button
          type="button"
          onClick={() => setTable((t) => !t)}
          aria-pressed={table}
          className="rounded-md border border-line px-2 py-0.5 text-xs hover:bg-page"
        >
          {table ? "Chart" : "Table"}
        </button>
      </div>

      {columns.length === 0 ? (
        <p className="mt-6 text-sm text-muted">No history in this date range.</p>
      ) : table ? (
        <SegmentTable columns={columns} />
      ) : (
        <div className="mt-7 flex gap-2">
          {/* y-axis: round ticks carry the values that are not labelled */}
          <div className="relative w-10 shrink-0 text-right text-xs text-muted tabular-nums" style={{ height: PLOT_HEIGHT }}>
            {ticks.map((t) => (
              <span key={t} className="absolute right-0 -translate-y-1/2" style={{ bottom: (t / top) * PLOT_HEIGHT }}>
                {tick(t)}
              </span>
            ))}
          </div>
          <div className="min-w-0 flex-1">
            <div className="relative" style={{ height: PLOT_HEIGHT }}>
              {ticks.map((t) => (
                <div
                  key={t}
                  aria-hidden
                  className={`absolute inset-x-0 h-px ${t === 0 ? "bg-axis" : "bg-grid"}`}
                  style={{ bottom: (t / top) * PLOT_HEIGHT }}
                />
              ))}
              <ol className="absolute inset-0 flex">
                {columns.map((c) => (
                  <Column key={c.label} segment={c} top={top} active={active === c.label} onActive={setActive} />
                ))}
              </ol>
            </div>
            <ol aria-hidden className="mt-1.5 flex text-xs text-ink-2">
              {columns.map((c) => (
                <li key={c.label} className={`flex-1 truncate text-center ${c.totals ? "" : "text-muted"}`}>
                  {c.label === "hot" ? "Last 3 mo" : c.label}
                </li>
              ))}
            </ol>
          </div>
        </div>
      )}
    </section>
  );
}

function Column({
  segment,
  top,
  active,
  onActive,
}: {
  segment: Segment;
  top: number;
  active: boolean;
  onActive: (label: string | null) => void;
}) {
  const t = segment.totals;
  const name = segmentName(segment.label);
  const description = t ? `${name}: ${count(t.count)} matches, ${moneyCompact(t.amount)}` : `${name}: still searching`;

  return (
    // The whole slot is the hit target, so a short bar is as easy to hover as a tall one.
    <li
      tabIndex={0}
      aria-label={description}
      className="group relative flex flex-1 cursor-default items-end justify-center outline-none"
      onPointerEnter={() => onActive(segment.label)}
      onPointerLeave={() => onActive(null)}
      onFocus={() => onActive(segment.label)}
      onBlur={() => onActive(null)}
    >
      {t ? (
        <div
          className={`w-full max-w-6 rounded-t-[4px] transition-[height,background-color] duration-300 ${
            active ? "bg-hover" : "bg-series-1"
          } group-focus-visible:ring-2 group-focus-visible:ring-series-1 group-focus-visible:ring-offset-2`}
          style={{ height: Math.max(t.count > 0 ? 2 : 0, (t.count / top) * PLOT_HEIGHT) }}
        />
      ) : (
        <span aria-hidden className="mb-1.5 size-2 animate-pulse rounded-full bg-muted" />
      )}
      {active && (
        <div
          role="tooltip"
          className="pointer-events-none absolute bottom-full z-10 mb-2 rounded-lg border border-line bg-surface px-3 py-2 text-sm whitespace-nowrap shadow-sm"
        >
          {t ? (
            <>
              <p className="font-semibold">{count(t.count)} matches</p>
              <p className="text-ink-2">
                {moneyCompact(t.amount)} · {span(segment)}
              </p>
            </>
          ) : (
            <p className="text-ink-2">{span(segment)} · still searching</p>
          )}
        </div>
      )}
    </li>
  );
}

/** "Nov 2019 – Dec 2019": the first and last year are partial, which explains their shorter bars. */
const span = (s: Segment) => {
  const [from, to] = [shortMonth(s.from), shortMonth(s.to)];
  return from === to ? from : `${from} – ${to}`;
};

function SegmentTable({ columns }: { columns: Segment[] }) {
  return (
    <table className="mt-4 w-full text-sm">
      <thead>
        <tr className="border-b border-line text-left text-ink-2">
          <th className="py-1.5 font-medium">Segment</th>
          <th className="py-1.5 text-right font-medium">Matches</th>
          <th className="py-1.5 text-right font-medium">Amount</th>
        </tr>
      </thead>
      <tbody className="tabular-nums">
        {[...columns].reverse().map((c) => (
          <tr key={c.label} className="border-b border-line last:border-0">
            <td className="py-1.5">{segmentName(c.label)}</td>
            <td className="py-1.5 text-right">{c.totals ? count(c.totals.count) : "…"}</td>
            <td className="py-1.5 text-right">{c.totals ? moneyCompact(c.totals.amount) : "…"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

import { CircleAlert, Undo2 } from "lucide-react";
import { count, duration, moneyCompact } from "../lib/format.ts";
import { isStale, searched, type SearchState } from "../lib/stream.ts";

/** Running totals that grow as each year reports, and how long the answer took to arrive. */
export function Summary({ state }: { state: SearchState }) {
  const { running, phase, segments } = state;
  const live = phase === "searching";
  const done = searched(state);
  const stale = isStale(state);

  return (
    <section aria-label="Totals" className={`space-y-3 transition-opacity ${stale ? "opacity-50" : ""}`}>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Tile label={live ? "Matches so far" : "Matches"} value={count(running.count)} />
        <Tile label="Amount" value={moneyCompact(running.amount)} />
        <Tile
          label="Exceptions"
          icon={<CircleAlert className="size-4 text-warning" aria-hidden />}
          value={count(running.by_status.exception ?? 0)}
        />
        <Tile
          label="Returned items"
          icon={<Undo2 className="size-4 text-critical" aria-hidden />}
          value={count(running.by_status.returned ?? 0)}
        />
      </div>

      <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm text-ink-2" aria-live="polite">
        <div className="flex items-center gap-2">
          <div
            className="h-1.5 w-32 overflow-hidden rounded-full bg-grid"
            role="progressbar"
            aria-label="Segments searched"
            aria-valuemin={0}
            aria-valuemax={segments.length}
            aria-valuenow={done}
          >
            <div
              className="h-full rounded-full bg-series-1 transition-[width] duration-200"
              style={{ width: `${segments.length ? (done / segments.length) * 100 : 0}%` }}
            />
          </div>
          <span className="tabular-nums">
            {done} of {segments.length} {segments.length === 1 ? "segment" : "segments"} searched
          </span>
        </div>
        {state.firstPageMs !== undefined &&
          (state.exactPageMs === undefined || state.exactPageMs - state.firstPageMs < 1 ? (
            <span>
              First page in <strong className="font-semibold text-ink">{duration(state.exactPageMs ?? state.firstPageMs)}</strong>
            </span>
          ) : (
            // The hot tier had a few matches but not a page: they show first, the exact page later.
            <>
              <span>
                First results in <strong className="font-semibold text-ink">{duration(state.firstPageMs)}</strong>
              </span>
              <span>
                Exact page in <strong className="font-semibold text-ink">{duration(state.exactPageMs)}</strong>
              </span>
            </>
          ))}
        {state.doneMs !== undefined && (
          <span>
            All history in <strong className="font-semibold text-ink">{duration(state.doneMs)}</strong>
          </span>
        )}
      </div>
    </section>
  );
}

function Tile({ label, value, icon }: { label: string; value: string; icon?: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-line bg-surface p-4">
      <p className="flex items-center gap-1.5 text-sm text-ink-2">
        {icon}
        {label}
      </p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
    </div>
  );
}

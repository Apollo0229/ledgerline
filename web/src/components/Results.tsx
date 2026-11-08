import { Fragment } from "react";
import { count, date, money } from "../lib/format.ts";
import { isStale, type SearchState } from "../lib/stream.ts";
import { StatusBadge } from "./StatusBadge.tsx";

const HEAD = ["Deposited", "Client", "Payer", "Check", "Invoices", "Amount", "Status"];

/** The newest page of matching payments. Skeleton rows only on the very first search. */
export function Results({ state }: { state: SearchState }) {
  const { hits, hasPage, pageComplete, phase, running } = state;
  const firstLoad = phase === "searching" && !hasPage && hits.length === 0;

  return (
    <section aria-labelledby="results" className="rounded-xl border border-line bg-surface">
      <div className="flex flex-wrap items-baseline justify-between gap-2 p-4 pb-2">
        <h2 id="results" className="font-semibold">
          Newest payments
        </h2>
        <p className="text-sm text-ink-2" aria-live="polite">
          {note(state)}
        </p>
      </div>

      <div className="overflow-x-auto">
        <table className={`w-full min-w-[56rem] text-sm transition-opacity ${isStale(state) ? "opacity-50" : ""}`}>
          <thead>
            <tr className="border-y border-line text-left text-ink-2">
              {HEAD.map((h) => (
                <th key={h} scope="col" className={`px-4 py-2 font-medium ${h === "Amount" ? "text-right" : ""}`}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {firstLoad &&
              Array.from({ length: 8 }, (_, i) => (
                <tr key={i} className="border-b border-line" aria-hidden>
                  {HEAD.map((h) => (
                    <td key={h} className="px-4 py-3">
                      <div className="skeleton h-3.5" style={{ width: `${50 + ((i * 7 + h.length * 13) % 45)}%` }} />
                    </td>
                  ))}
                </tr>
              ))}
            {hits.map((p) => (
              <tr key={p.id} className="border-b border-line last:border-0 hover:bg-page">
                <td className="px-4 py-2.5 whitespace-nowrap tabular-nums">{date(p.deposit_date)}</td>
                <td className="px-4 py-2.5">{p.client}</td>
                <td className="px-4 py-2.5">
                  {p.payer}
                  {/* A posted payment's memo only repeats its invoices; exceptions and returns explain themselves. */}
                  {p.status !== "posted" && <span className="block text-xs text-ink-2">{p.memo}</span>}
                </td>
                <td className="px-4 py-2.5 tabular-nums">{p.check_number}</td>
                <td className="px-4 py-2.5 text-xs text-ink-2 tabular-nums">
                  {/* Each number stays whole; lines break only between them. */}
                  {p.invoices.map((inv, i) => (
                    <Fragment key={inv}>
                      {i > 0 && ", "}
                      <span className="whitespace-nowrap">{inv}</span>
                    </Fragment>
                  ))}
                </td>
                <td className="px-4 py-2.5 text-right whitespace-nowrap tabular-nums">{money(p.amount)}</td>
                <td className="px-4 py-2.5">
                  <StatusBadge status={p.status} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {phase !== "searching" && hasPage && hits.length === 0 && (
        <p className="px-4 py-8 text-center text-sm text-ink-2">No payments match these filters.</p>
      )}
      {phase === "searching" && hasPage && !pageComplete && (
        <p className="px-4 py-3 text-sm text-ink-2">
          Older years are still being searched; more matches may join this page. ({count(running.count)} so far)
        </p>
      )}
    </section>
  );
}

function note({ phase, hasPage, hits, running, error }: SearchState): string {
  if (phase === "error") return error ?? "Search failed.";
  if (!hasPage) return phase === "searching" ? "Searching the newest months…" : "";
  if (hits.length === 0) return phase === "done" ? "0 matches" : "";
  const of = phase === "done" ? `of ${count(running.count)} matches` : "so far";
  return `Showing the newest ${count(hits.length)} ${of}`;
}

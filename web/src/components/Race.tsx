import { duration } from "../lib/format.ts";
import type { RecordedSearch } from "../lib/demo.ts";

/**
 * How long this search took, measured: Ledgerline's first page and complete answer against one plain
 * search delivering the same page, totals and per-year breakdown. Few bars, so each is labelled directly
 * with its value; the legend names the two systems.
 */
export function Race({ search }: { search: RecordedSearch }) {
  const rows = [
    { label: "First page", ms: search.first_page_ms, who: "ledgerline" as const },
    { label: "All totals", ms: search.done_ms, who: "ledgerline" as const },
    { label: "Page and totals", ms: search.baseline_ms, who: "plain" as const },
  ];
  const max = Math.max(...rows.map((r) => r.ms));
  const ratio = search.baseline_ms / search.first_page_ms;

  return (
    <figure className="rounded-xl border border-line bg-surface p-4">
      <figcaption className="flex flex-wrap items-baseline justify-between gap-2">
        <span>
          <span className="block font-semibold">Time to answer, measured</span>
          <span className="text-sm text-ink-2">
            {ratio >= 1.15
              ? `First page ${ratio.toFixed(1)}× sooner than one plain search`
              : "No faster here: the answer is not in the newest months"}
          </span>
        </span>
        <span className="flex items-center gap-4 text-xs text-ink-2">
          <Key className="bg-series-1" label="Ledgerline (streamed)" />
          <Key className="bg-series-2" label="One plain search" />
        </span>
      </figcaption>

      <dl className="mt-3 space-y-2">
        {rows.map((r) => (
          <div key={r.label} className="grid grid-cols-[7rem_1fr] items-center gap-3 text-sm">
            <dt className="text-ink-2">{r.label}</dt>
            <dd className="flex items-center gap-2">
              <span
                className={`h-4 rounded-r-[4px] ${r.who === "ledgerline" ? "bg-series-1" : "bg-series-2"}`}
                style={{ width: `${Math.max(1, (r.ms / max) * 85)}%` }}
                aria-hidden
              />
              <span className="tabular-nums">{duration(r.ms)}</span>
            </dd>
          </div>
        ))}
      </dl>
    </figure>
  );
}

function Key({ className, label }: { className: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={`size-2.5 rounded-sm ${className}`} aria-hidden />
      {label}
    </span>
  );
}

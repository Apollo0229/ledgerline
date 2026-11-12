import { History, RotateCcw } from "lucide-react";
import { describeFilters, type RecordedSearch, type Recording } from "../lib/demo.ts";
import { count } from "../lib/format.ts";

export const SLOWDOWNS = [
  { value: 10, label: "10× slower" },
  { value: 1, label: "Real speed" },
];

interface Props {
  recording: Recording;
  selected: RecordedSearch;
  slowdown: number;
  onSelect: (id: string) => void;
  onSlowdown: (s: number) => void;
  onReplay: () => void;
}

/** Says plainly that this is a recording, and lets the visitor pick a search and how fast to replay it. */
export function DemoBar({ recording, selected, slowdown, onSelect, onSlowdown, onReplay }: Props) {
  const recorded = new Date(recording.recorded_at).toLocaleDateString("en-US", { dateStyle: "medium" });
  const env = recording.environment;

  return (
    <section aria-label="Demo" className="space-y-3 rounded-xl border border-line bg-surface p-4">
      <p className="flex items-start gap-2 text-sm text-ink-2">
        <History className="mt-0.5 size-4 shrink-0 text-series-1" aria-hidden />
        <span>
          <span className="font-medium text-ink">Replay of real searches</span>, recorded {recorded} on a laptop
          running the full stack over {count(env.payments)} payments (Elasticsearch {env.elasticsearch_version}).{" "}
          {recording.note} Every number is what the stack reported; this page only plays it back.
        </span>
      </p>

      <div role="radiogroup" aria-label="Recorded searches" className="flex flex-wrap gap-2">
        {recording.searches.map((s) => (
          <button
            key={s.id}
            type="button"
            role="radio"
            aria-checked={s.id === selected.id}
            onClick={() => onSelect(s.id)}
            className={`rounded-full border px-3 py-1 text-sm ${
              s.id === selected.id ? "border-series-1 bg-series-1/10 font-medium" : "border-line hover:bg-page"
            }`}
          >
            {s.label}
          </button>
        ))}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line pt-3">
        <p className="text-sm text-ink-2">
          <span className="text-ink">Filters:</span> {describeFilters(selected.filters).join(" · ")}
          {selected.note && <span className="block text-xs">{selected.note}</span>}
        </p>
        <div className="flex items-center gap-1.5">
          {SLOWDOWNS.map((s) => (
            <button
              key={s.value}
              type="button"
              onClick={() => onSlowdown(s.value)}
              aria-pressed={slowdown === s.value}
              className={`rounded-md border px-2 py-0.5 text-xs ${
                slowdown === s.value ? "border-series-1 bg-series-1/10 font-medium" : "border-line hover:bg-page"
              }`}
            >
              {s.label}
            </button>
          ))}
          <button
            type="button"
            onClick={onReplay}
            className="inline-flex items-center gap-1 rounded-md border border-line px-2 py-0.5 text-xs hover:bg-page"
          >
            <RotateCcw className="size-3" aria-hidden />
            Replay
          </button>
        </div>
      </div>
    </section>
  );
}

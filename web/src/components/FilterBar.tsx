import { RotateCcw, Search } from "lucide-react";
import { EMPTY, type Filters, RANGES, type Range } from "../lib/filters.ts";
import { STATUSES, type Status } from "../lib/types.ts";
import { STATUS } from "./StatusBadge.tsx";

const field =
  "h-9 rounded-md border border-line bg-surface px-2.5 text-sm text-ink outline-none focus-visible:ring-2 focus-visible:ring-series-1";

interface Props {
  filters: Filters;
  clients: string[];
  problem: string | null;
  onChange: (f: Filters) => void;
}

/** One row of filters above everything they scope. Date range first: it is what people reach for. */
export function FilterBar({ filters, clients, problem, onChange }: Props) {
  const set = <K extends keyof Filters>(key: K, value: Filters[K]) => onChange({ ...filters, [key]: value });

  return (
    <form role="search" aria-label="Filter payments" className="space-y-2" onSubmit={(e) => e.preventDefault()}>
      <div className="flex flex-wrap items-end gap-2">
        <Labeled label="Deposited">
          <select className={field} value={filters.range} onChange={(e) => set("range", e.target.value as Range)}>
            {RANGES.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
        </Labeled>
        {filters.range === "custom" && (
          <>
            <Labeled label="From">
              <input type="date" className={field} value={filters.from} onChange={(e) => set("from", e.target.value)} />
            </Labeled>
            <Labeled label="To">
              <input type="date" className={field} value={filters.to} onChange={(e) => set("to", e.target.value)} />
            </Labeled>
          </>
        )}
        <Labeled label="Search" grow>
          <div className="relative">
            <Search className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-muted" aria-hidden />
            <input
              type="search"
              className={`${field} w-full pl-8`}
              placeholder="Payer, invoice, check number, memo"
              value={filters.text}
              maxLength={200}
              onChange={(e) => set("text", e.target.value)}
            />
          </div>
        </Labeled>
        <Labeled label="Client">
          <select className={`${field} max-w-56`} value={filters.client} onChange={(e) => set("client", e.target.value)}>
            <option value="">All clients</option>
            {clients.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </Labeled>
        <Labeled label="Status">
          <select className={field} value={filters.status} onChange={(e) => set("status", e.target.value as Status | "")}>
            <option value="">Any status</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {STATUS[s].label}
              </option>
            ))}
          </select>
        </Labeled>
        <Labeled label="Min $">
          <input inputMode="decimal" className={`${field} w-24`} value={filters.min} onChange={(e) => set("min", e.target.value)} />
        </Labeled>
        <Labeled label="Max $">
          <input inputMode="decimal" className={`${field} w-24`} value={filters.max} onChange={(e) => set("max", e.target.value)} />
        </Labeled>
        <button
          type="button"
          onClick={() => onChange(EMPTY)}
          className="inline-flex h-9 items-center gap-1.5 rounded-md px-2.5 text-sm text-ink-2 hover:bg-surface"
        >
          <RotateCcw className="size-4" aria-hidden />
          Reset
        </button>
      </div>
      {problem && (
        <p role="alert" className="text-sm text-ink-2">
          {problem}
        </p>
      )}
    </form>
  );
}

function Labeled({ label, grow, children }: { label: string; grow?: boolean; children: React.ReactNode }) {
  return (
    <label className={`flex flex-col gap-1 text-xs text-ink-2 ${grow ? "min-w-56 flex-1" : ""}`}>
      {label}
      {children}
    </label>
  );
}

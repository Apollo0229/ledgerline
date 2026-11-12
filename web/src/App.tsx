import { useEffect, useMemo, useState } from "react";
import { DemoBar } from "./components/DemoBar.tsx";
import { FilterBar } from "./components/FilterBar.tsx";
import { Race } from "./components/Race.tsx";
import { Results } from "./components/Results.tsx";
import { Summary } from "./components/Summary.tsx";
import { YearChart } from "./components/YearChart.tsx";
import { DEMO, type Recording } from "./lib/demo.ts";
import { type Filters, fromSearch, problem, toApiQuery, toSearch } from "./lib/filters.ts";
import type { SearchState } from "./lib/stream.ts";
import { useDemoSearch } from "./useDemoSearch.ts";
import { useSearch } from "./useSearch.ts";

export const App = DEMO ? DemoApp : LiveApp;

/** The portal over a running API. */
function LiveApp() {
  const [filters, setFilters] = useState<Filters>(() => fromSearch(window.location.search));
  const [clients, setClients] = useState<string[]>([]);
  const settled = useDebounced(filters, 300);
  const invalid = problem(settled);

  // Keep the address bar in step with the filters, so any search can be bookmarked or shared.
  useEffect(() => {
    const qs = toSearch(settled);
    window.history.replaceState(null, "", qs ? `?${qs}` : window.location.pathname);
  }, [settled]);

  useEffect(() => {
    fetch(`${import.meta.env.BASE_URL}v1/clients`)
      .then((r) => (r.ok ? (r.json() as Promise<string[]>) : []))
      .then(setClients)
      .catch(() => setClients([]));
  }, []);

  const query = useMemo(() => (invalid ? null : toApiQuery(settled, new Date())), [settled, invalid]);
  const state = useSearch(query);

  return (
    <Shell>
      <FilterBar filters={filters} clients={clients} problem={problem(filters)} onChange={setFilters} />
      <Answer state={state} />
    </Shell>
  );
}

/** The public demo: searches recorded from the real stack, replayed through the same components. */
function DemoApp() {
  const [recording, setRecording] = useState<Recording | null>(null);
  const [failed, setFailed] = useState(false);
  const [selectedId, setSelectedId] = useState(() => new URLSearchParams(window.location.search).get("search"));
  const [slowdown, setSlowdown] = useState(10);
  const [run, setRun] = useState(0);

  useEffect(() => {
    fetch(`${import.meta.env.BASE_URL}demo/recording.json`)
      .then((r) => (r.ok ? (r.json() as Promise<Recording>) : Promise.reject(new Error(r.statusText))))
      .then(setRecording)
      .catch(() => setFailed(true));
  }, []);

  const selected = recording?.searches.find((s) => s.id === selectedId) ?? recording?.searches[0] ?? null;
  const state = useDemoSearch(selected, slowdown, run);

  const select = (id: string) => {
    setSelectedId(id);
    window.history.replaceState(null, "", `?search=${encodeURIComponent(id)}`);
  };

  return (
    <Shell>
      {failed && <p role="alert" className="rounded-lg border border-line bg-surface p-3 text-sm">Could not load the recording.</p>}
      {recording && selected && (
        <>
          <DemoBar
            recording={recording}
            selected={selected}
            slowdown={slowdown}
            onSelect={select}
            onSlowdown={setSlowdown}
            onReplay={() => setRun((r) => r + 1)}
          />
          <Race search={selected} />
        </>
      )}
      <Answer state={state} />
      <section className="rounded-xl border border-line bg-surface p-4 text-sm">
        <p className="text-ink-2">Run it yourself, live, with every filter:</p>
        <pre className="mt-2 overflow-x-auto font-mono text-xs">
          git clone https://github.com/Apollo0229/ledgerline{"\n"}cd ledgerline && docker compose up -d --wait
        </pre>
      </section>
    </Shell>
  );
}

/** Totals, the chart and the newest payments: the same for a live search and a replay. */
function Answer({ state }: { state: SearchState }) {
  return (
    <>
      {state.phase === "error" && (
        <p role="alert" className="rounded-lg border border-line bg-surface p-3 text-sm">
          {state.error}
        </p>
      )}
      <Summary state={state} />
      <YearChart state={state} />
      <Results state={state} />
    </>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex max-w-7xl flex-wrap items-baseline justify-between gap-2 px-4 py-4 sm:px-6">
          <div>
            <h1 className="text-xl font-semibold">Ledgerline</h1>
            <p className="text-sm text-ink-2">Seven years of lockbox deposits, searched newest first</p>
          </div>
          <a className="text-sm text-ink-2 underline-offset-4 hover:underline" href="https://github.com/Apollo0229/ledgerline">
            Source on GitHub
          </a>
        </div>
      </header>
      <main className="mx-auto max-w-7xl space-y-5 px-4 py-5 sm:px-6">{children}</main>
    </div>
  );
}

/** The value once it has stopped changing for `ms`, so typing doesn't start a search per keystroke. */
function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return settled;
}

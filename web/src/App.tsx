import { useEffect, useMemo, useState } from "react";
import { FilterBar } from "./components/FilterBar.tsx";
import { Results } from "./components/Results.tsx";
import { Summary } from "./components/Summary.tsx";
import { YearChart } from "./components/YearChart.tsx";
import { type Filters, fromSearch, problem, toApiQuery, toSearch } from "./lib/filters.ts";
import { useSearch } from "./useSearch.ts";

export function App() {
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

      <main className="mx-auto max-w-7xl space-y-5 px-4 py-5 sm:px-6">
        <FilterBar filters={filters} clients={clients} problem={problem(filters)} onChange={setFilters} />
        {state.phase === "error" && (
          <p role="alert" className="rounded-lg border border-line bg-surface p-3 text-sm">
            {state.error}
          </p>
        )}
        <Summary state={state} />
        <YearChart state={state} />
        <Results state={state} />
      </main>
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

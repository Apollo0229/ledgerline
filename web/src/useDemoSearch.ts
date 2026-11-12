import { useEffect, useReducer } from "react";
import { type RecordedSearch, schedule, toAction } from "./lib/demo.ts";
import { initial, reduce, type SearchState } from "./lib/stream.ts";

/**
 * Replays a recorded search through the same reducer the live portal uses, so every component shows it
 * exactly as it showed the real stream. `slowdown` stretches the replay (10 = ten times slower) without
 * changing the timings it reports. Changing `run` replays the same search again.
 */
export function useDemoSearch(search: RecordedSearch | null, slowdown: number, run: number): SearchState {
  const [state, dispatch] = useReducer(reduce, initial);

  useEffect(() => {
    if (!search) return;
    dispatch({ type: "start", now: 0 });
    const timers = schedule(search.events, slowdown).map(({ at, e }) =>
      setTimeout(() => dispatch(toAction(e, 0)), at),
    );
    return () => timers.forEach(clearTimeout);
  }, [search, slowdown, run]);

  return state;
}

import { useEffect, useReducer } from "react";
import { initial, reduce, type SearchState } from "./lib/stream.ts";
import type { DoneEvent, PageEvent, PlanEvent, ProgressEvent } from "./lib/types.ts";

/**
 * Runs a streamed search whenever `query` changes. Changing the query or leaving the page closes the
 * EventSource, which closes the connection, and the API cancels the years it had not searched yet.
 * Pass null to search nothing (for example while the filters are invalid).
 */
export function useSearch(query: string | null): SearchState {
  const [state, dispatch] = useReducer(reduce, initial);

  useEffect(() => {
    if (query === null) return;
    dispatch({ type: "start", now: performance.now() });

    const source = new EventSource(`${import.meta.env.BASE_URL}v1/search/stream?${query}`);
    const on = <T,>(type: string, handle: (data: T) => void) =>
      source.addEventListener(type, (e) => {
        if (e instanceof MessageEvent && typeof e.data === "string") handle(JSON.parse(e.data) as T);
      });

    on<PlanEvent>("plan", (event) => dispatch({ type: "plan", event }));
    on<PageEvent>("page", (event) => dispatch({ type: "page", event, now: performance.now() }));
    on<ProgressEvent>("progress", (event) => dispatch({ type: "progress", event }));
    on<DoneEvent>("done", (event) => {
      // Close straight away: when the server ends the stream, EventSource would otherwise reconnect
      // and quietly run the whole search again.
      source.close();
      dispatch({ type: "done", event, now: performance.now() });
    });

    // The server's own "error" event and the browser's connection error share a name.
    source.addEventListener("error", (e) => {
      source.close();
      const message =
        e instanceof MessageEvent && typeof e.data === "string"
          ? (JSON.parse(e.data) as { message: string }).message
          : "Lost the connection to the search API. Is it running?";
      dispatch({ type: "error", message });
    });

    return () => source.close();
  }, [query]);

  return state;
}

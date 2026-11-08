// Shapes of the API's JSON. Field names follow the API's snake_case.

export type Status = "posted" | "exception" | "returned";
export const STATUSES: Status[] = ["posted", "exception", "returned"];

export interface Payment {
  id: string;
  deposit_date: string;
  client: string;
  lockbox: string;
  payer: string;
  check_number: string;
  amount: number;
  invoices: string[];
  status: Status;
  memo: string;
}

export interface Totals {
  count: number;
  amount: number;
  by_status: Partial<Record<Status, number>>;
}

export interface PlannedSegment {
  label: string;
  from: string;
  to: string;
}

export interface PlanEvent {
  segments: PlannedSegment[];
  at_ms: number;
}

export interface PageEvent {
  hits: Payment[];
  complete: boolean;
  at_ms: number;
}

export interface ProgressEvent {
  segment: string;
  from: string;
  to: string;
  segment_totals: Totals;
  running: Totals;
  searched: number;
  segments: number;
  at_ms: number;
}

export interface DoneEvent {
  totals: Totals;
  first_page_ms: number | null;
  at_ms: number;
}

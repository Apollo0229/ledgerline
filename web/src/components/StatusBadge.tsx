import { CircleAlert, CircleCheck, type LucideIcon, Undo2 } from "lucide-react";
import type { Status } from "../lib/types.ts";

/** Status colors are reserved for state and always travel with an icon and a label, never color alone. */
export const STATUS: Record<Status, { label: string; icon: LucideIcon; tone: string }> = {
  posted: { label: "Posted", icon: CircleCheck, tone: "text-good" },
  exception: { label: "Exception", icon: CircleAlert, tone: "text-warning" },
  returned: { label: "Returned", icon: Undo2, tone: "text-critical" },
};

export function StatusBadge({ status }: { status: Status }) {
  const { label, icon: Icon, tone } = STATUS[status];
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <Icon className={`size-4 shrink-0 ${tone}`} aria-hidden />
      <span>{label}</span>
    </span>
  );
}

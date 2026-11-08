namespace Ledgerline.Core;

/// <summary>
/// One step of a streamed search. <see cref="AtMs"/> is the time since the search started, so the
/// moment the first page arrived can be read straight off the stream.
/// </summary>
public abstract record SearchEvent(long AtMs)
{
    /// <summary>The Server-Sent Events event name.</summary>
    public abstract string Kind { get; }
}

/// <summary>
/// The first page as known so far, newest first. It replaces any earlier page event. When
/// <see cref="Complete"/> is true no later segment can change it: every unsearched document is older.
/// </summary>
public sealed record PageEvent(long AtMs, IReadOnlyList<Payment> Hits, bool Complete) : SearchEvent(AtMs)
{
    public override string Kind => "page";
}

/// <summary>One more segment of history searched: its own totals and the running totals so far.</summary>
public sealed record ProgressEvent(
    long AtMs,
    string Segment,
    DateOnly From,
    DateOnly To,
    Totals SegmentTotals,
    Totals Running,
    int Searched,
    int Segments) : SearchEvent(AtMs)
{
    public override string Kind => "progress";
}

/// <summary>Every segment searched. <see cref="Totals"/> equals what one query over the whole range returns.</summary>
public sealed record DoneEvent(long AtMs, Totals Totals, long? FirstPageMs) : SearchEvent(AtMs)
{
    public override string Kind => "done";
}

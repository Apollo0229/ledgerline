using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Ledgerline.Core;

namespace Ledgerline.Api;

internal static partial class SearchStream
{
    /// <summary>
    /// Turns search events into Server-Sent Events named <c>page</c>, <c>progress</c> and <c>done</c>.
    /// Once the stream has started the status code is already 200, so a failure part way through is
    /// reported as a final <c>error</c> event instead of a silently truncated stream.
    /// </summary>
    public static async IAsyncEnumerable<SseItem<object>> ToSse(
        IAsyncEnumerable<SearchEvent> events, ILogger logger, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var e = events.GetAsyncEnumerator(ct);
        while (true)
        {
            SearchEvent next;
            try
            {
                if (!await e.MoveNextAsync()) yield break;
                next = e.Current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
                next = null!;
            }

            if (next is null)
            {
                yield return new SseItem<object>(new { message = "Search failed part way through. Try again." }, "error");
                yield break;
            }
            yield return new SseItem<object>(next, next.Kind);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Streamed search failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

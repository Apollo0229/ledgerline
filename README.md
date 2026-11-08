# Ledgerline

Fast search over years of lockbox payments. An ASP.NET Core API over Elasticsearch
that answers the first page from the hot tier in milliseconds and streams the rest
of the answer to a React UI over Server-Sent Events while cold history is searched.

> Work in progress. See the milestones below.

## The problem

A treasury client asks: *"Show me every check from Acme Supply over $5,000 in the
last seven years."* The data is years of lockbox deposits: one index per month,
most of it old and rarely read. A single query across all of it makes the person
wait for the slowest, coldest shard before they see anything.

## The idea

Results are sorted newest first. So if the **hot tier** (the last three months)
already holds a full page of matches, that page is the **exact** first page of the
whole answer, because every cold document is older. Ledgerline:

1. searches the hot tier and streams page 1 at once,
2. searches cold history year by year, newest first, streaming running totals
   (count, sum, status breakdown) as each year finishes,
3. stops early when the person cancels or changes the query.

The baseline, one query across every index, is kept as an endpoint so the
difference can be measured rather than claimed.

## Stack

| Part | Tech |
| --- | --- |
| API | C# / ASP.NET Core (.NET 10), minimal APIs, Server-Sent Events |
| Search | Elasticsearch 9 (monthly indices, hot and cold tiers) via `Elastic.Clients.Elasticsearch` |
| Seeder | .NET console app: deterministic synthetic lockbox payments, bulk indexed |
| UI | React + TypeScript + Vite |
| Tests | xUnit v3; integration tests against a real Elasticsearch, checked against LINQ over the same data |

## Milestones

- [x] 1. **Index and seed**: index template, monthly indices, hot/cold tiers, deterministic seeder, baseline search endpoint
- [x] 2. **Two-phase streamed search**: hot page first, cold years streamed over SSE, cancellation
- [ ] 3. **Search portal**: React UI with filters, skeletons and live totals
- [ ] 4. **Measured**: baseline vs two-phase, time to first result and p95 under load
- [ ] 5. **Public demo**

## How the data is laid out

- **One index per deposit month** (`payments-2026.11`), created from a strict
  [index template](src/Ledgerline.Search/payments-template.json). Each index is
  sorted by deposit date, newest first.
- **Hot tier**: the newest three months. Still written to, refreshed every second.
- **Cold tier**: everything older. Made read-only and force-merged to a single
  segment once loaded, which keeps years of rarely-read history cheap to store
  and search.
- **Synthetic payments**: 30 treasury clients, thousands of payers, log-normal
  amounts around $1,800, about 95.5% posted, 3.5% exceptions and 1% returned
  items. Volume grows over time. Each month is generated from its own seed, so
  the data is identical on every machine and re-seeding overwrites by id.

## Run it

Needs Docker and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
docker compose up -d                                      # Elasticsearch on localhost:9200
dotnet run -c Release --project src/Ledgerline.Seeder     # 2,000,000 payments, about a minute
dotnet run -c Release --project src/Ledgerline.Api        # http://localhost:5000
```

The seeder takes `--count`, `--today` and `--parallel`; re-running it skips
months that are already complete.

```sh
curl 'localhost:5000/v1/search?text=INV-244258'
curl 'localhost:5000/v1/search?client=Meridian%20Freight%20Lines&min=5000&status=exception&from=2021-01-01'
```

| Parameter | Meaning |
| --- | --- |
| `text` | Free text over payer, memo, invoice, check number and lockbox |
| `client` | Treasury client, exact name |
| `status` | `posted`, `exception` or `returned` |
| `min`, `max` | Amount range in dollars |
| `from`, `to` | Deposit date range, `YYYY-MM-DD` |
| `size` | Page size, 0 to 200 (default 50) |

## Streamed search

`GET /v1/search/stream` takes the same parameters and answers in stages over
[Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html):

```text
$ curl -N 'localhost:5000/v1/search/stream?status=returned&min=2000'

event: page        {"hits":[...50 newest...], "complete":true, "at_ms":15}
event: progress    {"segment":"hot",  "segment_totals":{"count":302,...}, "running":{"count":302,...},  "searched":1, "segments":9}
event: progress    {"segment":"2026", "segment_totals":{"count":1129,...},"running":{"count":1431,...}, "searched":2, "segments":9}
...
event: progress    {"segment":"2019", ...                                  "running":{"count":9368,...}, "searched":9, "segments":9}
event: done        {"totals":{"count":9368, "amount":69027222.04, ...}, "first_page_ms":15, "at_ms":33}
```

| Event | When | Meaning |
| --- | --- | --- |
| `page` | as soon as there is something to show | The first page so far, newest first; replaces any earlier `page`. `complete: true` means no later segment can change it. |
| `progress` | after each segment | That segment's totals and the running totals. Segments are the hot tier, then each cold year, newest first. |
| `done` | at the end | Final totals, equal to what `/v1/search` returns for the same filters. |
| `error` | if a segment fails mid-stream | The stream ends here; filters are validated before it starts, so bad input is still a `400`. |

How it works:

- **Page 1 is exact early.** If the hot tier has a full page of matches, nothing
  older can displace it, so it is sent before any cold index is touched. If
  not, the page is topped up from the newest cold years.
- **Years run three at a time, applied in order.** An older year that answers
  first waits its turn, so the page never takes an older match early.
- **Once the page is full, later years return totals only** (`size: 0`).
- **Closing the connection stops the work.** Searches in flight are cancelled
  and the remaining years are never started.

Tests check that, for several kinds of query (hot-heavy, rare, all-cold, free
text, no matches), the streamed page and totals equal the single-query
baseline exactly. Timings are measured properly in milestone 4.

## Develop

```sh
dotnet test
```

Unit tests always run. Integration tests seed 20,000 payments under their own
`payments-test-` prefix, so they never touch seeded data, and compare every
search with the same payments filtered in memory. Without a running cluster
they are skipped; CI sets `REQUIRE_ELASTICSEARCH=1` so they fail instead.

## License

MIT

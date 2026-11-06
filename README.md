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
- [ ] 2. **Two-phase streamed search**: hot page first, cold years streamed over SSE, cancellation
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

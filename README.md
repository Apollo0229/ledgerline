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
| Search | Elasticsearch 9 (monthly indices, hot and cold aliases) via `Elastic.Clients.Elasticsearch` |
| Seeder | .NET console app: deterministic synthetic lockbox payments, bulk indexed |
| UI | React + TypeScript + Vite |
| Tests | xUnit, plus integration tests against a real Elasticsearch |

## Milestones

- [ ] 1. **Index and seed**: index template, monthly indices, hot/cold aliases, deterministic seeder, baseline search endpoint
- [ ] 2. **Two-phase streamed search**: hot page first, cold years streamed over SSE, cancellation
- [ ] 3. **Search portal**: React UI with filters, skeletons and live totals
- [ ] 4. **Measured**: baseline vs two-phase, time to first result and p95 under load
- [ ] 5. **Public demo**

## Run it

```sh
docker compose up -d
```

## License

MIT

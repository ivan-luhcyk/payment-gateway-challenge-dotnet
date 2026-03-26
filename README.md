# Payment Gateway

An ASP.NET Core 8 payment gateway that accepts card payments, forwards them to an acquiring bank simulator, and stores the result. Built as a coding challenge implementation.

---

## Engineering Focus

> During a prior interview stage the feedback highlighted that **scale is the core challenge at Checkout.com**. This implementation was deliberately designed with that in mind.

Some decisions drive the architecture:

**1. Asynchronous bank processing via RabbitMQ**

The `POST /api/payments` endpoint returns immediately with `Processing` status. Bank authorization happens asynchronously — the HTTP request is never blocked waiting for the bank. This means:
- The API can handle a high volume of payment submissions without being bottlenecked by bank latency
- Bank-processing consumers can be scaled independently from the API layer
- If the bank is slow or unavailable, messages queue in RabbitMQ and drain when the bank recovers — no requests are dropped

**2. Stateless API instances backed by shared Redis**

No state is held in-process. All API instances read and write to the same Redis store, so any number of instances can run behind a load balancer with no coordination needed. Adding capacity is as simple as starting another container.

**3. CQRS — separate read and write paths**

Commands (`PostPaymentCommand`, `ProcessBankPaymentCommand`) and queries (`GetPaymentQuery`) are modelled as distinct message types handled by separate consumers. This is not just a pattern choice — it directly enables scale:

- **Write path** (POST) and **read path** (GET) can be scaled independently. If read traffic dwarfs write traffic (which is typical — merchants poll for payment status far more than they submit payments), you scale GET consumers without touching the write side
- With a SQL database in place, the write path commits to a **primary (write) replica** and the read path queries a **read replica**. Because reads and writes are already separated at the code level, routing them to different database connections requires no architectural change — only an infrastructure one
- CQRS also makes it straightforward to introduce a dedicated read model later (e.g. a denormalised Redis projection optimised for GET queries) without changing how payments are written

**Storage evolution path**

Redis is currently used as the primary store for payments and idempotency keys. This is intentional for the challenge scope, but the architecture anticipates the need to introduce a SQL database as the source of truth:

- `IPaymentRepository` and `IIdempotencyStore` are interfaces — the Redis implementation (`PaymentsRepository`) can be replaced or supplemented without touching any business logic
- The natural next step is to add a SQL-backed `IPaymentRepository` writing to a **primary replica**, with reads served from a **read replica** via a separate read-optimised implementation
- Redis stays in the stack as a read cache and idempotency store — the SQL database becomes the durable source of truth, Redis keeps latency low for repeated status polls
- No handler, controller, or domain class knows about Redis or SQL — the swap is purely an infrastructure concern

---

## Table of Contents

- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [How to Run](#how-to-run)
- [Running Tests](#running-tests)
- [API Reference](#api-reference)
- [Resilience](#resilience)

---

## Architecture

The system follows **Domain-Driven Design** with a **CQRS** messaging pattern, using **MassTransit** over **RabbitMQ** as the message bus.

```
┌─────────────────────────────────────────────────────────┐
│                    HTTP Client                          │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│               PaymentsController                        │
│  - Validates idempotency header                         │
│  - Validates request model                              │
│  - Maps HTTP → Command/Query                            │
└──────────┬──────────────────────────┬───────────────────┘
           │ PostPaymentCommand        │ GetPaymentQuery
┌──────────▼──────────┐    ┌──────────▼──────────────────┐
│PostPaymentCommand   │    │ GetPaymentQueryHandler       │
│Handler              │    │  - Reads from Redis          │
│  - Idempotency check│    │  - Returns PaymentResponse   │
│  - Creates Payment  │    │    or PaymentNotFound        │
│  - Saves to Redis   │    └─────────────────────────────-┘
│  - Publishes event  │
└──────────┬──────────┘
           │ ProcessBankPaymentCommand (async, via RabbitMQ)
┌──────────▼──────────────────────────────────────────────┐
│         ProcessBankPaymentCommandHandler                │
│  - Calls BankClient (HTTP + Polly resilience)           │
│  - Transitions Payment status                           │
│  - Saves updated Payment to Redis                       │
└──────────┬──────────────────────────────────────────────┘
           │
┌──────────▼──────────────────────────────────────────────┐
│              Bank Simulator (Mountebank)                │
│              POST http://bank_simulator:8080/payments   │
└─────────────────────────────────────────────────────────┘
```

### Payment lifecycle

```
POST /api/payments
       │
       ▼
  [Processing]  ← returned immediately to caller
       │
       ▼ (async via RabbitMQ)
  Bank call
       ├── authorized: true  → [Authorized]
       ├── authorized: false → [Declined]
       └── error / 503       → [Rejected]
```

The `POST` response always returns `Processing`. The caller polls `GET /api/payments/{id}` to get the final status.

---

## Project Structure

```
src/
  PaymentGateway.Api/
    Controllers/          — HTTP layer only (routing, status codes, model mapping)
    Application/
      Handlers/           — MassTransit consumers (business logic)
      Messages/           — Commands and queries (plain records)
    Domain/
      Payments/           — Aggregate root (Payment), value objects (CardInfo, CardDetails, Money)
      Bank/               — Bank result types
      Interfaces/         — IBankClient, IPaymentRepository, IIdempotencyStore
      Enums/              — PaymentStatus
    Infrastructure/
      Bank/               — BankClient (HTTP), BankPaymentRequest/Response, BankSimulatorOptions
      Persistence/        — PaymentsRepository (Redis), RedisOptions
      Messaging/          — RabbitMqOptions
    Presentation/         — IdempotencyOptions, Swagger filters
    Models/
      Requests/           — PostPaymentRequest (validation model)
      Responses/          — PaymentResponse (DTO)

test/
  PaymentGateway.Api.Tests/
    Unit/                 — Handler and bank client unit tests (NSubstitute mocks)
    Integration/          — Full HTTP tests via WebApplicationFactory (in-memory transport + fake bank)

imposters/                — Mountebank bank simulator config (do not modify)
docker-compose.yml        — Runs API, Redis, RabbitMQ, bank simulator
```

---

## How to Run

### Option 1 — Docker Compose (recommended)

Starts everything: API, Redis, RabbitMQ, and bank simulator.

```bash
docker-compose up --build
```

| Service | URL |
|---|---|
| API | http://localhost:5067 |
| Swagger | http://localhost:5067/swagger |
| RabbitMQ management | http://localhost:15672 (guest/guest) |
| Bank simulator | http://localhost:8080 |

### Option 2 — Run locally

Start infrastructure first:

```bash
docker-compose up redis rabbitmq bank_simulator
```

Then run the API:

```bash
cd src/PaymentGateway.Api
dotnet run
```

API available at http://localhost:5067 and https://localhost:7092.

---

## Running Tests

Tests require no external infrastructure — they use an **in-memory MassTransit transport** and a **fake bank client**.

```bash
dotnet test
```

### Test structure

| Layer | Type | What is tested |
|---|---|---|
| `Unit/Application` | Unit | Each handler in isolation — NSubstitute mocks for all dependencies |
| `Unit/Domain` | Unit | Payment aggregate state transitions |
| `Integration` | Integration | Full HTTP pipeline via `WebApplicationFactory` — real controller, real handlers, fake bank |

### What we would add for production

**Contract tests**
Verify the bank simulator request/response contract using a tool like [Pact](https://docs.pact.io/). If the bank changes their API shape (field renamed, new required field), a contract test catches it before it reaches production. Currently the only protection is the integration test hitting the fake bank — which mirrors our assumptions, not the bank's real contract.

**Load and performance tests**
Use [k6](https://k6.io/) or [NBomber](https://nbomber.com/) to establish baseline throughput and latency under realistic concurrent load. Key scenarios:
- Sustained high POST volume to verify RabbitMQ queuing holds up
- Burst of GET requests (status polling) to verify Redis read performance
- Gradual ramp-up to find the point at which the circuit breaker opens under bank degradation

**End-to-end tests against a real environment**
Run a full stack (real Redis, real RabbitMQ, real bank simulator) using Docker Compose in CI and exercise the complete payment lifecycle — submit, poll until final status, assert the correct terminal state. These tests catch infrastructure wiring issues that `WebApplicationFactory` cannot (e.g. RabbitMQ queue configuration, Redis serialisation round-trips).

---

## Observability

Structured logging is in place across all layers using `ILogger<T>` with named properties (e.g. `PaymentId`, `IdempotencyKey`, `StatusCode`). In production this would feed into a centralised logging and observability stack.

### Logging

Every significant event is logged at the appropriate level:

| Level | Examples |
|---|---|
| `Information` | Payment created, idempotency hit, bank authorized/declined, payment retrieved |
| `Warning` | Missing idempotency header, invalid request, payment not found, bank non-success status |
| `Error` | Unhandled exception during bank authorization |

Polly resilience events (retry attempts, circuit breaker state changes, timeouts) are logged automatically by the framework.

### What we would add for production

**Distributed tracing**
Instrument with [OpenTelemetry](https://opentelemetry.io/) and export traces to Jaeger or Zipkin. A single payment spans multiple hops — HTTP → RabbitMQ → bank HTTP call — and a trace would show the full end-to-end latency broken down per step. MassTransit has built-in OpenTelemetry support, so propagating the trace context across the message bus requires minimal configuration.

**Metrics**
Expose key business and infrastructure metrics via OpenTelemetry or Prometheus:
- Payment throughput (submissions per second)
- Terminal status breakdown — rate of `Authorized` vs `Declined` vs `Rejected`
- Bank call latency (p50, p95, p99)
- RabbitMQ queue depth — a growing queue signals that bank processing consumers are falling behind
- Circuit breaker state — alert when the circuit opens

**Alerting**
Set threshold alerts on:
- `Rejected` rate spike — may indicate a bank outage or a breaking change in the bank API
- RabbitMQ queue depth growing beyond a threshold — consumers not keeping up
- Circuit breaker open — bank is down
- Error log rate increase — unexpected exceptions

**Correlation ID**
Propagate a correlation ID (from the `Idempotency-Key` or a dedicated `X-Correlation-ID` header) through every log line, message, and outbound HTTP call. This makes it possible to reconstruct the full journey of a single payment across all services and log sinks.

---

## API Reference

### `POST /api/payments`

Submit a new payment for processing.

**Headers**

| Header | Required | Description |
|---|---|---|
| `Idempotency-Key` | Yes | Unique key to prevent duplicate payments |
| `Content-Type` | Yes | `application/json` |

**Request body**

```json
{
  "cardNumber": "2222405343248877",
  "expiryMonth": 4,
  "expiryYear": 2030,
  "currency": "GBP",
  "amount": 1050,
  "cvv": "123"
}
```

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Payment accepted, status is `Processing` |
| `400 Bad Request` | Missing idempotency key or invalid request body |

**Response body**

```json
{
  "id": "a1b2c3d4-...",
  "status": "Processing",
  "cardNumberLastFour": 8877,
  "expiryMonth": 4,
  "expiryYear": 2030,
  "currency": "GBP",
  "amount": 1050
}
```

---

### `GET /api/payments/{id}`

Retrieve a payment by its ID.

**Responses**

| Status | Meaning |
|---|---|
| `200 OK` | Payment found |
| `404 Not Found` | No payment with that ID |

**Payment statuses**

| Status | Meaning |
|---|---|
| `Processing` | Accepted, bank call pending |
| `Authorized` | Bank approved |
| `Declined` | Bank refused |
| `Rejected` | Request failed validation, or bank was unreachable/returned an error |
| `Rejected` | Request failed validation |

---

## Resilience

Bank calls are protected by a **Polly** pipeline configured via `appsettings.json`:

```
Request → Timeout → Retry → Circuit Breaker → Bank
```

| Strategy | Default | Behaviour |
|---|---|---|
| **Timeout** | 10 s per attempt | Cancels the attempt if the bank is slow |
| **Retry** | 5 attempts, exponential back-off + jitter | Retries on 503 or network errors |
| **Circuit breaker** | Opens at 50% failure rate over 30 s | Stops calling the bank when it is clearly down, fails fast |

When the circuit is open or all retries are exhausted, the payment transitions to `Rejected` — the client can query the status and decide whether to retry the payment.

All resilience events (retry attempts, circuit state changes, timeouts) are logged automatically by Polly.

Configuration in `appsettings.json`:

```json
"BankSimulator": {
  "BaseUrl": "http://localhost:8080",
  "TimeoutSeconds": 10,
  "RetryCount": 5,
  "RetryDelayMs": 300,
  "CircuitBreakerMinimumThroughput": 5,
  "CircuitBreakerBreakSeconds": 30
}
```

---
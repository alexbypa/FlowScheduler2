# FlowScheduler

Distributed task orchestration engine with embedded AI agents, RAG pipeline, and multi-LLM routing. Built on **.NET 10** with strict SOLID architecture.

FlowScheduler monitors infrastructure, executes background jobs, and uses AI to diagnose failures autonomously — routing prompts to the optimal LLM, injecting relevant documentation via vector search, and exposing tools that agents can call to query databases, search knowledge bases, and interact with external services.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FlowScheduler.Core                           │
│         Entities, Interfaces, DTOs — zero external dependencies     │
└───────────────────────────────┬─────────────────────────────────────┘
                                │ depends on
┌───────────────────────────────▼─────────────────────────────────────┐
│                    FlowScheduler.Infrastructure                     │
│  AI Pipeline │ Redis │ RabbitMQ │ Database │ Telegram │ MCP Client  │
└──────┬────────────────────────────────────────────────┬─────────────┘
       │                                                │
┌──────▼──────────────┐                  ┌──────────────▼─────────────┐
│ FlowScheduler.WebApi│                  │FlowScheduler.BackgroundJobs│
│ Minimal API         │                  │ Hangfire Worker            │
│ Hangfire Dashboard  │                  │ Telegram Bot               │
│ MCP Server          │                  │ RabbitMQ Consumer          │
│ React SPAs          │                  │ Health Monitor             │
└─────────────────────┘                  └────────────────────────────┘
```

**4 projects, strict dependency flow.** Core has zero NuGet dependencies. Infrastructure implements all technology details. WebApi and BackgroundJobs are deployment targets that compose services via extension methods — `Program.cs` stays clean.

### Solution at a Glance

| Project | Files | Role |
|---------|-------|------|
| **Core** | 52 | Domain layer — entities, interfaces, DTOs, configuration POCOs |
| **Infrastructure** | 53 | Concrete implementations — AI, Redis, RabbitMQ, database, MCP |
| **WebApi** | 26 | HTTP entry point — Minimal API, Hangfire dashboard, MCP server, React SPAs |
| **BackgroundJobs** | 17 | Async worker — Hangfire jobs, Telegram polling, queue consumers |
| **WebApi.Tests** | 19 | Integration tests — xUnit, Testcontainers, NSubstitute |
| **CryptoStream.Worker** | 9 | Microservice — Binance WebSocket reader, RabbitMQ publisher |

---

## AI Pipeline

FlowScheduler implements a **7-layer AI pipeline** orchestrated by `AiPipelineExtension`:

```
Transport → ChatClient → Storage → Tools → Middleware → Agents → Consumers
```

### Multi-LLM Routing

Prompts are routed to the optimal model at runtime using `ComplexityAdvisorStrategy`:

| Tier | Provider | When |
|------|----------|------|
| **Primary** | Gemini (Google Vertex AI) | Complex reasoning, multi-step analysis |
| **Local** | Ollama | Lightweight tasks, no rate limits, fallback |
| **Auto** | Per-request decision | Strategy evaluates token count, complexity, history |

`FallbackChatClient` ensures resilience: if the primary LLM fails, the request falls back to Ollama automatically.

### Chat Pipeline Filters

Every LLM call passes through a filter chain built with `Microsoft.Extensions.AI`:

| Filter | Purpose |
|--------|---------|
| `ContextLimitFilter` | Sliding window — only the last 10 messages reach the LLM |
| `RagKnowledgeFilter` | Injects relevant RAG documents as system messages before each call |
| `ConnectionTracingFilter` | Traces HTTP connections to LLM services for observability |
| `SqlReadOnlyMiddleware` | Blocks INSERT/UPDATE/DELETE — AI can only read databases |
| `ToolCallLoggingMiddleware` | Audit trail for every tool invocation |

### AI Tools (Function Calling)

Agents can invoke 6 registered tools during reasoning:

| Tool | Capability |
|------|------------|
| `DatabaseTool` | Execute parameterized queries on SQL Server or PostgreSQL |
| `SqlTool` | Read-only SQL execution with result caching via `RedisContentStore` |
| `SqlDiagnosticsTool` | SQL validation and error analysis without execution |
| `RagTool` | Semantic search on the knowledge base |
| `DiagnosticTool` | Introspect Hangfire jobs, Redis state, system health |
| `GitHubTool` | Fetch repo metrics, DORA metrics, code scanning results |

### Agent Orchestration

Agents are defined in `appsettings.json` and registered via `ConfigurableAgentsExtension`. Each agent has:
- A system prompt defining its role
- A set of tools it can call
- A model tier (Primary/Local/Auto)
- Optional middleware pipeline

`JobDiagnosticAgent` is the core agent — it integrates Hangfire job context with AI reasoning to diagnose task failures autonomously.

---

## RAG Pipeline (Retrieval-Augmented Generation)

FlowScheduler grounds AI responses on real documentation using a three-stage RAG pipeline:

### Stack

| Component | Technology | Config |
|-----------|------------|--------|
| **Embedding** | Google `text-embedding-004` | 3072 dimensions |
| **Vector Store** | Redis + RediSearch | HNSW algorithm, COSINE distance |
| **Search** | KNN on `rag_idx` index | Top-K: 3, threshold: 0.45 |

### Pipeline Stages

**1. Ingestion** (`RagIngestionService`)
```
Text → Truncate (max 8000 chars) → Google Embedding API → Redis HASH (rag:doc:{id})
```
Supports single document, batch ingestion with parallel embeddings, and library documents with markdown.

**2. Search** (`RagSearchService`)
```
Query → Truncate (max 500 chars) → Embedding → KNN on rag_idx → Top-3 results (>55% similarity)
```

**3. Auto-Injection** (`RagKnowledgeFilter`)
Every LLM call is intercepted. The filter embeds the user's last message, runs a KNN search, and injects matching documents as system messages — the LLM sees relevant context without the user requesting it.

### Context Tags

| Tag | Purpose |
|-----|---------|
| `ops` | Operational docs — error diagnostics, task failures |
| `library` | Knowledge base — technical documentation, guides |

### HTTP Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/rag/ingest` | Ingest single ops document |
| POST | `/rag/ingest/batch` | Batch ingestion with parallel embeddings |
| POST | `/rag/search` | Manual vector search |
| POST | `/rag/confirm-solution` | Learn a verified AI solution |
| GET | `/rag/library/documents` | Paginated document listing with filters |
| POST | `/rag/library/documents` | Create library document |

A **React SPA** at `/rag/library` provides a UI for browsing and managing the knowledge base.

---

## MCP (Model Context Protocol)

FlowScheduler acts as both **MCP client and server**:

### MCP Server (WebApi)

Exposes tools via the MCP protocol for external AI agents:

| Tool | Endpoint |
|------|----------|
| `RagSearchMcpTool` | Semantic search on knowledge base |
| `RagDocumentsMcpTool` | List and query RAG documents |
| `MetricsQueryMcpTool` | Query performance metrics |
| `DiagnosticMcpTool` | System diagnostics |

### MCP Client (Infrastructure)

Connects to external MCP servers (e.g., `ProjectPulse` for GitHub health analysis). Configured in `appsettings.json` under `McpServerOptions.McpServers`.

---

## Background Jobs & Async Processing

### Hangfire

All background work runs through **Hangfire** with Redis storage. Jobs implement `IJobCommand` (Command Pattern):

| Command | Responsibility |
|---------|----------------|
| `DatabaseExecuteCommand` | Execute scheduled DB queries with optional AI analysis of results |
| `HttpCallCommand` | Scheduled HTTP requests (webhooks, health checks) |
| `RabbitMqConsumerCommand` | Generic queue consumer — any RabbitMQ queue to any database table |

`BackgroundJobHandler` dispatches jobs to the correct `IJobCommand` via `InjectCommandFactory` (Factory Pattern). Jobs run with `PerformContext` for Hangfire console logging.

### Health Monitoring

`HealthMonitorService` runs on a cron schedule, collecting:
- GitHub repository metrics (via `GitHubTool`)
- DORA metrics
- Code scanning results
- System diagnostics

Results are ingested into the RAG knowledge base via `HealthRagBridgeService`, making historical health data searchable by AI agents.

---

## Messaging

### RabbitMQ

Generic message consumption — FlowScheduler consumes JSON from any queue and writes to any database table:

```
RabbitMQ Queue → RabbitMqConsumerService (batch, ack/nack) → DynamicJsonDbWriter → Database
```

- **No domain-specific code** — table name and queue are job parameters
- SQL injection prevention via regex validation on table names
- Batch processing with configurable prefetch count

### Telegram Bot

`TelegramPollingService` (IHostedService) runs a long-poll loop. Messages are handled by `TelegramChatHandler`, which routes them through the AI pipeline — users chat with FlowScheduler's AI agents directly from Telegram.

---

## Redis (6 Roles)

Redis is the backbone of FlowScheduler, serving six distinct roles:

| Role | Implementation | TTL |
|------|---------------|-----|
| **Vector Store** | `RedisVectorStoreService` — RediSearch HNSW index | Permanent |
| **Chat History** | `RedisChatHistoryStore` — sliding window of 20 messages | 4 hours |
| **Content Store** | `RedisContentStore` — bulk payloads (>10KB) | 48 hours |
| **Metrics Store** | `RedisMetricsStore` — Sorted Sets for performance telemetry | Configurable |
| **Job Storage** | Hangfire Redis storage | Per Hangfire config |
| **Distributed Cache** | `IDistributedCache` via StackExchange.Redis | Per entry |

---

## Database Support

Multi-database via abstract factory pattern (`IDbConnectionFactory` → `IDbConnectionFactoryResolver`):

| Database | Factory | Usage |
|----------|---------|-------|
| SQL Server | `SqlServerConnectionFactory` | Primary data store |
| PostgreSQL | `PostgreSqlConnectionFactory` | Alternative data store |

AI tools interact with databases through `SqlReadOnlyMiddleware` — agents can query but never mutate data.

---

## SOLID Architecture

### Dependency Injection

17 extension methods keep `Program.cs` clean. Each feature registers its own services:

```csharp
// Program.cs stays minimal
services.AddAiPipeline(configuration);       // 7-layer AI stack
services.AddRabbitMqInfrastructure(configuration); // RabbitMQ consumer
services.AddMonitoring(configuration);       // Health checks + DORA
services.AddMetrics(configuration);          // Performance telemetry
```

### Key Patterns

| Pattern | Where | Purpose |
|---------|-------|---------|
| **Strategy** | `IAdvisorStrategy` → `ComplexityAdvisorStrategy` | Per-request LLM routing |
| **Factory** | `IChatClientFactory` → `AdvisorChatClientFactory` | LLM client creation by tier |
| **Command** | `IJobCommand` → `DatabaseExecuteCommand`, `HttpCallCommand` | Job type dispatch |
| **Adapter** | `IMetricsStore` → `RedisMetricsStore` | Redis Sorted Sets as metrics backend |
| **Abstract Factory** | `IDbConnectionFactory` → `SqlServer/PostgreSql` | Multi-DB support |
| **Decorator** | `FallbackChatClient`, `RagKnowledgeFilter` | Chat pipeline composition |
| **Observer** | `CommandObservable` | Real-time state transitions |

### Boundaries

- **Core** never references Infrastructure, WebApi, or BackgroundJobs
- **Infrastructure** only references Core
- **No cross-technology pollution** — Telegram code can't touch Hangfire config, SQL tools can't touch RAG logic
- **1 class = 1 file** — no exceptions
- **Async methods** end with `Async`

---

## Microservices

### CryptoStream.Worker

Independent .NET 10 Worker Service that reads real-time crypto trades from Binance WebSocket and publishes to RabbitMQ:

```
Binance WebSocket → BinanceWebSocketReader → JSON normalization → RabbitMqPublisher → crypto.trades queue
```

- Exponential backoff reconnect on WebSocket failures
- `IAsyncDisposable` for clean shutdown
- Health probe via `WebSocketHealthCheck`
- Dockerfile with multi-stage build

FlowScheduler's `RabbitMqConsumerCommand` picks up messages from `crypto.trades` and writes them to the database — zero crypto domain knowledge in FlowScheduler itself.

---

## Tech Stack

| Category | Technology | Version |
|----------|-----------|---------|
| **Runtime** | .NET | 10.0 |
| **AI Abstraction** | Microsoft.Extensions.AI | 9.7.0-preview |
| **Agent Framework** | Microsoft.Agents.AI | 1.10.0 |
| **Local LLM** | Ollama (via Microsoft.Extensions.AI.Ollama) | Latest |
| **Cloud LLM** | Google Gemini (via Vertex AI) | 1.0.0-beta07 |
| **Embeddings** | Google text-embedding-004 | 3072 dim |
| **MCP** | ModelContextProtocol | 2.2.0 |
| **Vector Store** | Redis + RediSearch | HNSW/COSINE |
| **Message Broker** | RabbitMQ | 7.2.2 client |
| **Job Scheduler** | Hangfire | 1.8.23 |
| **Cache** | Redis (StackExchange) | Latest |
| **SQL Server** | Microsoft.Data.SqlClient | 7.0.0-preview |
| **PostgreSQL** | Npgsql | 10.0.3 |
| **HTTP Resilience** | Microsoft.Extensions.Http.Resilience | 10.6.0 |
| **Telegram** | Telegram.Bot | Latest |
| **Testing** | xUnit + Testcontainers + NSubstitute | Latest |
| **Logging** | CSharpEssentials.LoggerHelper | 5.0.x |

---

## Infrastructure

### Docker Compose

```bash
docker compose up -d
```

| Service | Port | Purpose |
|---------|------|---------|
| **redis-cache** | 6380, 8001 | Redis + RediSearch (vector store, cache, Hangfire storage) |
| **ollama** | 11434 | Local LLM inference |
| **rabbitmq** | 5672, 15672 | Message broker + management UI |
| **grafana** | 3001 | Metrics dashboard |
| **projectpulse** | 3000 | GitHub health analysis MCP server |
| **flow-webapi** | 5000 | FlowScheduler API |
| **flow-worker** | — | Background job worker |

### Kubernetes

Production-ready manifests in `k8s/`:

```
k8s/
├── rabbitmq/          # Deployment + PVC (1Gi) + Service
├── crypto-stream/     # Deployment + initContainer + ConfigMap
├── flowscheduler/     # Deployment + Service (80→8080) + ConfigMap
└── network-policies/  # Least-privilege pod communication
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### Run

```bash
# Start infrastructure
docker compose up -d

# Run the API
dotnet run --project FlowScheduler.WebApi

# Run the background worker (separate terminal)
dotnet run --project FlowScheduler.BackgroundJobs
```

### Dashboards

| URL | What |
|-----|------|
| `http://localhost:5000/hangfire` | Hangfire dashboard — jobs, retries, queues |
| `http://localhost:5000/rag/library` | RAG knowledge base browser (React SPA) |
| `http://localhost:5000/mcp-playground` | MCP tool testing UI |
| `http://localhost:8001` | Redis Insight — browse keys, indexes, vectors |
| `http://localhost:15672` | RabbitMQ management — queues, exchanges, consumers |
| `http://localhost:3001` | Grafana — metrics dashboards |

---

## Project Structure

```
FlowScheduler/
├── FlowScheduler.Core/
│   ├── Configuration/       # Options POCOs
│   ├── Dtos/                # Request/Response contracts
│   ├── Entities/            # MonitorTask, ScheduledTask
│   ├── Interfaces/
│   │   ├── AI/              # 12 abstractions (IChatClientFactory, IRagSearchService, ...)
│   │   ├── Data/            # IDbConnectionFactory, IMonitorRepository
│   │   ├── Jobs/            # IJobCommand, ITaskSchedulerService
│   │   ├── Messaging/       # ITelegramService
│   │   ├── Monitoring/      # IHealthMonitorService, IMetricsStore
│   │   └── Processing/      # IResultProcessor, IResultProcessorFactory
│   ├── Models/              # VectorStoreConstants
│   └── Shared/              # ErrorContextFormatter
├── FlowScheduler.Infrastructure/
│   ├── AI/
│   │   ├── Advisor/         # LLM routing strategy
│   │   ├── Agents/          # Configurable agent orchestration
│   │   ├── ChatClient/      # Pipeline filters (context limit, RAG, fallback)
│   │   ├── Middleware/      # SQL safety, tool call logging
│   │   ├── RAG/             # Ingestion, search, vector store, health bridge
│   │   ├── Registry/        # Tool registry
│   │   ├── Storage/         # Redis chat history + content store
│   │   ├── Tools/           # 6 AI-callable tools
│   │   └── Transport/       # HTTP resilience
│   ├── Database/            # SQL Server + PostgreSQL factories
│   ├── Messaging/           # Telegram + RabbitMQ
│   ├── Monitoring/          # Health monitor + DORA metrics
│   └── Processing/          # Result processors
├── FlowScheduler.WebApi/
│   ├── McpTools/            # MCP server tool endpoints
│   ├── MinimalApi/
│   │   ├── RAG/             # REST endpoints for RAG operations
│   │   ├── CustomDashboardPages/  # Hangfire custom pages
│   │   └── SettingTasks/    # Task management
│   └── wwwroot/
│       ├── rag-library/     # React SPA
│       └── mcp-playground/  # MCP testing UI
├── FlowScheduler.BackgroundJobs/
│   ├── Jobs/                # IJobCommand implementations
│   ├── Consumers/           # Telegram chat handler
│   ├── Extensions/          # DI registration
│   └── Hosting/             # Telegram polling service
├── FlowScheduler.WebApi.Tests/  # Integration tests
├── Microservices/
│   └── CryptoStream.Worker/ # Binance → RabbitMQ microservice
├── k8s/                     # Kubernetes manifests
├── docs/                    # Architecture documentation
└── docker-compose.yml
```

---

## License

This project is for educational and portfolio purposes.

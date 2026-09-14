# FlowScheduler

Distributed task management and monitoring engine automated via Local Agentic AI (MEAI).

## Stack

- **.NET 10** — Core runtime
- **Hangfire** — Background job orchestration (Redis storage)
- **Redis** — Cache, vector store (RediSearch HNSW), chat history, content store
- **Ollama** — Local LLM inference
- **Gemini** — Primary cloud LLM (with Ollama fallback)
- **Google text-embedding-004** — Embedding generation (3072 dim)

## Solution Structure

| Project | Role |
|---------|------|
| `FlowScheduler.Core` | Domain layer — entities, interfaces, DTOs. Zero external dependencies |
| `FlowScheduler.Infrastructure` | Implementations — AI pipeline, Redis, database, messaging |
| `FlowScheduler.WebApi` | HTTP entry point — Minimal API, Hangfire dashboard, RAG library SPA |
| `FlowScheduler.BackgroundJobs` | Async worker — Hangfire jobs, Telegram bot, Redis consumers |

## Key Features

- **AI Advisory Routing** — Per-request model selection (Gemini vs Ollama) based on task complexity
- **RAG Pipeline** — Document ingestion, vector search, automatic context injection into LLM prompts
- **Configurable Agents** — JSON-driven agent orchestration with tool registry
- **SQL Safety** — Read-only middleware blocks DML/DDL on AI-accessible database tools
- **Multi-DB Support** — SQL Server, PostgreSQL, Cosmos DB via abstract factory

## Documentation

Architecture and operational docs live in `docs/`:
- `docs/ai-core/dotnet-boundaries.md` — Project boundaries, namespace map, SOLID rules
- `docs/ai-core/ai-rag-infrastructure.md` — RAG stack parameters and endpoints
- `docs/ai-core/session-state.md` — Context window and state management
- `docs/vibe-coding/vibe-directives.md` — AI agent autonomy and execution rules

Task tracking: `outcomes/TODO.md`

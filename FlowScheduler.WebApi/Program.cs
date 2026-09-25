using CSharpEssentials.HttpHelper;
using CSharpEssentials.LoggerHelper;
using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Infrastructure.AI;
using FlowScheduler.Infrastructure.AI.RAG;
using FlowScheduler.Infrastructure.AI.Registry;
using FlowScheduler.Infrastructure.AI.Storage;
using FlowScheduler.Infrastructure.Jobs;
using FlowScheduler.Infrastructure.MCP;
using FlowScheduler.Infrastructure.Metrics;
using FlowScheduler.Infrastructure.Persistence;
using FlowScheduler.Infrastructure.Processing;
using FlowScheduler.WebApi.Configuration;
using FlowScheduler.WebApi.McpTools;
using FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;
using FlowScheduler.WebApi.MinimalApi.health;
using FlowScheduler.WebApi.MinimalApi.HttpHelper.mocks;
using FlowScheduler.WebApi.MinimalApi.Metrics;
using FlowScheduler.WebApi.MinimalApi.RAG;
using FlowScheduler.WebApi.MinimalApi.SettingTasks;
using Hangfire;
using Hangfire.Console;
using Hangfire.Dashboard;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using System.Reflection;


var builder = WebApplication.CreateBuilder(args);

//TODO: Ottimizzare l' iniezione delle dipendenze con scrutor !

// 1. CSharpEssentials.LoggerHelper
builder.Configuration.AddJsonFile("appsettings.LoggerHelper.json", optional: true, reloadOnChange: true);
builder.Services.AddHangfireConsoleSink(); // registra IPerformContextAccessor come singleton
builder.Services.AddLoggerHelper(builder.Configuration);

// 2. Iniezione dei mock per HttpHelper (per testare le chiamate HTTP senza fare richieste reali)
builder.Services.InjectMocks();
builder.Services.AddHttpClients(builder.Configuration);
builder.Services.AddTransient<InspectingHandler>();
builder.Services.AddHttpClient("AiChatClient").AddHttpMessageHandler<InspectingHandler>();

// 3. Aggiunta Custom Tabs su Dashboard Hangfire (RAG Library e MCP Playground)
CustomDashboardExtensions.RegisterCustomDashboardPages();


// 4. Assicurati che anche Redis sia registrato, altrimenti il Manager fallirà
builder.Services.AddAppRedis(builder.Configuration);

// 5. Configurazione Hangfire con Redis
builder.Services.AddAppHangfire();

// --- AI PIPELINE + RAG + MCP ---
// Configurazione chiave API (necessaria per LLM e embeddings)
var (aiSettings, apiKeySource) = AiSettingsConfiguration.Resolve(builder.Configuration);
AiSettingsConfiguration.EnsureApiKeyOrThrow(aiSettings);
builder.Services.AddSingleton(aiSettings);

// Full AI Pipeline (7 layer: Transport → ChatClient → Storage → Tools → Middleware → Agents → Consumers)
// Registra IChatClientFactory, IRagBridgeService, IRagIngestionService, IVectorStoreService, etc.
builder.Services.AddAiPipeline(builder.Configuration);

// Metrics Store: Fornisce i dati (lettura da Redis) per popolare i grafici all'endpoint /metrics/query
builder.Services.AddMetricsStore();

// MCP Server: Espone questa WebApi verso l'esterno (es. Claude Desktop) per usare i tool come MetricsQueryMcpTool
builder.Services.AddMcpServices();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options => {
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});





// --- 1. Pipeline HTTP
var app = builder.Build();

// --- 2. Log della chiave API risolta per OpenAI (solo suffisso e lunghezza, non la chiave completa)
AiSettingsConfiguration.LogResolvedKey(aiSettings, apiKeySource, app.Logger);

app.UseRouting();
// --- 3. CUSTOM DASHBOARD REDIRECTS (per SPA) ---
app.UseCustomDashboardRedirects();
app.UseStaticFiles();

// --- 3. SWAGGER (Deve stare PRIMA degli endpoint personalizzati) ---
app.UseSwagger();
app.UseSwaggerUI(c => {
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FlowScheduler API V1");
    c.RoutePrefix = "swagger"; // Swagger sarà su http://localhost:5000/swagger
});

// --- 4. ENDPOINT APPLICATIVI (Minimal API) ---
app.MaphealthEndpoints();
app.MapTaskEndpoints();
app.MapMetricLibraryEndpoints();
app.MapMcpEndpoints();
app.MapRagEndpoints();

// --- 5. HANGFIRE DASHBOARD (Mappata come ENDPOINT) ---
// RIMUOVI app.UseHangfireDashboard(...) - causa il crash in .NET 9
app.MapAppHangfireDashboard();

app.Run();

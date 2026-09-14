using CSharpEssentials.HttpHelper;
using CSharpEssentials.LoggerHelper;
using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Infrastructure.AI.RAG;
using FlowScheduler.Infrastructure.AI.Storage;
using FlowScheduler.Infrastructure.Configuration;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Infrastructure.MCP;
using FlowScheduler.Infrastructure.Metrics;
using FlowScheduler.Infrastructure.Persistence;
using FlowScheduler.Infrastructure.Processing;
using FlowScheduler.WebApi.Configuration;
using FlowScheduler.WebApi.McpTools;
using FlowScheduler.WebApi.MinimalApi;
using FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;
using FlowScheduler.WebApi.MinimalApi.HttpHelper.mocks;
using Google.Api;
using Hangfire;
using Hangfire.Console;
using Hangfire.Dashboard;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.AI;
using OpenAI;
using StackExchange.Redis;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;
using FlowScheduler.Infrastructure.AI.Registry;

DotEnvLoader.LoadFromRepositoryRoot();
var builder = WebApplication.CreateBuilder(args);

//TODO: includere CsharpEssential.Redis !
//TODO: Ottimizzare l' iniezione delle dipendenze con scrutor !
//TODO: Con Task:whenAll aspettiamo la risposta dal DB dei prezzi e aspettiamo la chiamata Web API per confrontare i dati !

builder.Configuration.AddJsonFile("appsettings.LoggerHelper.json", optional: true, reloadOnChange: true);

builder.Services.AddHangfireConsoleSink(); // registra IPerformContextAccessor come singleton
builder.Services.AddLoggerHelper(builder.Configuration);

//TODO: da controllare !!!!! DRY !
builder.Services.AddScoped<IToolRegistry, ToolRegistry>();


builder.Services.InjectMocks();
builder.Services.AddHttpClients(builder.Configuration);
builder.Services.AddTransient<InspectingHandler>();
builder.Services.AddHttpClient("AiChatClient").AddHttpMessageHandler<InspectingHandler>();

DashboardRoutes.Routes.Add("/rag-library-dash", new MyPageDispatcher(_ => new RagLibraryDashboardRedirectPage()));
NavigationMenu.Items.Add(page => new MenuItem("Libreria RAG", page.Url.To("/rag-library-dash")) {
    Active = page.RequestPath.StartsWith("/rag-library-dash")
});

DashboardRoutes.Routes.Add("/mcp-playground-dash", new MyPageDispatcher(_ => new McpPlaygroundDashboardRedirectPage()));
NavigationMenu.Items.Add(page => new MenuItem("MCP Playground", page.Url.To("/mcp-playground-dash")) {
    Active = page.RequestPath.StartsWith("/mcp-playground-dash")
});

builder.Services.AddScoped<IResultProcessor, ResultProcessorFromDatabase>();

// 3. Assicurati che anche Redis sia registrato, altrimenti il Manager fallirà
var redisOptions = builder.Configuration.GetSection("redisCacheOptions").Get<RedisCacheOptions>();
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => {
    var configOptions = new ConfigurationOptions {
        EndPoints = { $"{redisOptions.Host}:{redisOptions.Port}" },
        Password = redisOptions.Password,
        AbortOnConnectFail = false, // Evita crash all'avvio se Redis è giù
        ConnectTimeout = redisOptions.ConnectTimeout,
        SyncTimeout = redisOptions.SyncTimeout,
        ConnectRetry = 5
    };
    return ConnectionMultiplexer.Connect(configOptions);
});


builder.Services.AddMCPClient(builder.Configuration);

//builder.Services.AddHttpClient(); //TODO: temp da sostituire con CSharpEssentials.HttpHelper !

builder.Services.AddHangfire((sp, configuration) => {
    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();

    configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseColouredConsoleLogProvider()
    .UseRedisStorage(multiplexer, new RedisStorageOptions {
        Db = 0,
        Prefix = "hangfire:"
    })
    .UseConsole();
});
// Il processing dei job è gestito dal worker FlowScheduler.BackgroundJobs.
// AddHangfireServer non serve qui: la dashboard e la schedulazione funzionano con solo AddHangfire.


//builder.Services.AddHangFireInfrastructure(builder.Configuration);
builder.Services.AddScoped<ITaskSchedulerService, MonitorHangFireService>();

// RAG Services — stesso provider embedding usato dai BackgroundJobs (Google.GenAI SDK)
var (aiSettings, apiKeySource) = AiSettingsConfiguration.Resolve(builder.Configuration);
AiSettingsConfiguration.EnsureApiKeyOrThrow(aiSettings);
builder.Services.AddSingleton(aiSettings);

builder.Services.AddAiStorage(aiSettings);
builder.Services.AddTransient<IRagIngestionService, RagIngestionService>();
builder.Services.AddMetricsStore();
builder.Services.AddMcpServices();

builder.Services.AddEndpointDefinitions(); // registra gli endpoint via IEndpointDefinition
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(options => {
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});
var app = builder.Build();
AiSettingsConfiguration.LogResolvedKey(aiSettings, apiKeySource, app.Logger);

// --- 0. MCP CLIENT — Registra tool da server MCP esterni ---
await app.Services.GetRequiredService<IMcpClientService>().RegisterAllServersAsync();

// --- 1. MIDDLEWARE DI BASE ---
app.UseRouting();
// Evita cicli /rag-library <-> /rag-library/ tra DefaultFiles e StaticFiles: vai direttamente a index.html
app.Use(async (context, next) => {
    var p = context.Request.Path.Value ?? "";
    if (string.Equals(p, "/rag-library", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p, "/rag-library/", StringComparison.OrdinalIgnoreCase)) {
        context.Response.Redirect("/rag-library/index.html");
        return;
    }

    if (string.Equals(p, "/mcp-playground", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p, "/mcp-playground/", StringComparison.OrdinalIgnoreCase)) {
        context.Response.Redirect("/mcp-playground/index.html");
        return;
    }

    await next();
});
app.UseStaticFiles();

// --- 3. SWAGGER (Deve stare PRIMA degli endpoint personalizzati) ---
app.UseSwagger();
app.UseSwaggerUI(c => {
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FlowScheduler API V1");
    c.RoutePrefix = "swagger"; // Swagger sarà su http://localhost:5000/swagger
});

// --- 4. ENDPOINT APPLICATIVI (Minimal API) ---
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
app.UseEndpointDefinitions(); // Questo mappa il tuo MapPost("/tasks")
app.MapMcpEndpoints();

// --- 5. HANGFIRE DASHBOARD (Mappata come ENDPOINT
// finale) ---
// RIMUOVI app.UseHangfireDashboard(...) - causa il crash in .NET 9
app.MapHangfireDashboard("/dashboard", new DashboardOptions {
    Authorization = new[] { new MyDashboardAuthorizationFilter() }
});

app.Run();

//TODO: posto sbagliato !
public class MyDashboardAuthorizationFilter : IDashboardAuthorizationFilter {
    public bool Authorize(DashboardContext context) {
        // In sviluppo, permettiamo l'accesso a tutti
        // In produzione (AZ-305), qui controlleresti i cookie o i ruoli JWT
        return true;
    }
}

public class InspectingHandler(ILogger<InspectingHandler> logger) : DelegatingHandler {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        logger.LogInformation("[AI DEBUG REQUEST] {Method} {RequestUri}", request.Method, request.RequestUri);

        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("[AI DEBUG ERROR RESPONSE] Status: {StatusCode} Body: {ErrorBody}", response.StatusCode, errorBody);
        }

        return response;
    }
}
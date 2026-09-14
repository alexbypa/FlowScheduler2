using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Registry;

/// <summary>
/// Registry concreto dei tool AI. Mappa nomi stringa a factory di AIFunction.
/// Ogni tool viene risolto dal DI container e wrappato con AIFunctionFactory.Create().
///
/// SOLID — OCP: per aggiungere un tool basta chiamare Register() al startup.
/// SOLID — SRP: responsabilita unica = risolvere tool per nome.
/// SOLID — DIP: implementa IToolRegistry (astrazione in Core).
///
/// Performance:
/// - Il dizionario delle factory e costruito una volta al startup (Singleton).
/// - La risoluzione a runtime e O(1) dictionary lookup + risoluzione DI scoped.
/// - Nessuna reflection a runtime, nessuna allocazione extra.
/// </summary>
public sealed class ToolRegistry : IToolRegistry {
    private readonly Dictionary<string, Func<IServiceProvider, AIFunction>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger) {
        _logger = logger;
    }

    /// <summary>
    /// Registra un tool con nome e factory. Chiamato al startup dalla pipeline DI.
    /// </summary>
    public void Register(string name, Func<IServiceProvider, AIFunction> factory) {
        _factories[name] = factory;
        _logger.LogDebug("[ToolRegistry] Tool registrato: {ToolName}", name);
    }

    /// <inheritdoc />
    public AIFunction? Resolve(string toolName, IServiceProvider sp) {
        if (_factories.TryGetValue(toolName, out var factory)) {
            _logger.LogDebug("[ToolRegistry] Risoluzione tool: {ToolName}", toolName);
            return factory(sp);
        }

        _logger.LogWarning("[ToolRegistry] Tool non trovato: {ToolName}. Tool disponibili: {Available}",
            toolName, string.Join(", ", _factories.Keys));
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableTools => _factories.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Registra solo i tool effettivamente referenziati da almeno un agente abilitato.
    /// Tool non referenziati nella configurazione non vengono registrati nel DI,
    /// evitando che appaiano nei log e riducendo la superficie di attacco.
    /// </summary>
    public void RegisterDefaults(IReadOnlySet<string> enabledToolNames) {
        // Catalogo completo: nome -> factory
        var catalog = new Dictionary<string, Func<IServiceProvider, AIFunction>>(StringComparer.OrdinalIgnoreCase) {
            ["Rag"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<RagTool>().SearchKnowledgeBaseAsync),
            ["GitHub"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<GitHubTool>().ReadGitHubCode),
            ["Diagnostic"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<DiagnosticTool>().GetLogDetails),
            ["Database"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<DatabaseTool>().GetDatabaseErrors),
            ["Sql"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<SqlTool>().ExecuteQueryAndStoreAsync),
            ["SqlDiagnostics"] = sp => AIFunctionFactory.Create(sp.GetRequiredService<SqlDiagnosticsTool>().GetSqlDiagnosticData),
        };

        foreach (var (name, factory) in catalog) {
            if (enabledToolNames.Contains(name)) {
                Register(name, factory);
            } else {
                _logger.LogDebug("[ToolRegistry] Tool '{ToolName}' non referenziato da agenti abilitati, skip", name);
            }
        }

        _logger.LogInformation("[ToolRegistry] {Count}/{Total} tool registrati (abilitati): {Names}",
            _factories.Count, catalog.Count, string.Join(", ", _factories.Keys));
    }
}

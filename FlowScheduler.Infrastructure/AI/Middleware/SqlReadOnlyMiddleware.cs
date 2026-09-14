using System.Text.RegularExpressions;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Middleware;
/// <summary>
/// Middleware di sicurezza. Intercetta ogni invocazione di tool e, se trova un parametro SQL (sqlQuery, query, sql), verifica che sia una SELECT pura: blocca INSERT/UPDATE/DELETE/DROP
///  e commenti SQL.È la guardia che impedisce all'AI di modificare il database.
/// </summary>
public class SqlReadOnlyMiddleware : IAgentMiddleware {
    private readonly ILogger<SqlReadOnlyMiddleware> _logger;

    // Keyword pericolose da bloccare come parola intera (\b = word boundary)
    private static readonly Regex DangerousKeywords = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|DROP|ALTER|CREATE|EXEC|EXECUTE|INTO\s+\w+\s+FROM|INTO\s+#|INTO\s+@|xp_\w+|sp_\w+|OPENROWSET|OPENQUERY|OPENDATASOURCE|GRANT|REVOKE|DENY|SHUTDOWN|DBCC|BULK\s+INSERT)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Blocca query multiple (SQL injection via statement stacking)
    private static readonly Regex MultiStatement = new(
        @";|\b(UNION\s+ALL\s+SELECT|UNION\s+SELECT)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Blocca accesso a cataloghi di sistema — impedisce all'AI di scoprire lo schema del database
    private static readonly Regex SystemCatalog = new(
        @"\b(sys\.\w+|INFORMATION_SCHEMA\.\w+|sysobjects|syscolumns|sysindexes|systypes|sysusers|sysprocesses)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tool che eseguono query SQL — solo questi vengono validati
    private static readonly HashSet<string> SqlToolNames = new(StringComparer.OrdinalIgnoreCase) {
        "ExecuteQueryAndStoreAsync", "GetSqlDiagnosticData", "GetDatabaseErrors"
    };

    // Nomi dei parametri che contengono query SQL nei tool
    private static readonly HashSet<string> SqlParameterNames = new(StringComparer.OrdinalIgnoreCase) {
        "sqlQuery", "sql"
    };

    public SqlReadOnlyMiddleware(ILogger<SqlReadOnlyMiddleware> logger) {
        _logger = logger;
    }

    /// <summary>
    /// Validates a SQL query string for read-only safety.
    /// Returns null if the query is safe (SELECT-only), or an error message if blocked.
    /// </summary>
    public static string? ValidateSqlQuery(string? rawQuery) {
        var query = rawQuery?.Trim() ?? string.Empty;

        if (!query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "ERRORE DI SICUREZZA: Sono permesse solo query di tipo SELECT.";

        if (DangerousKeywords.IsMatch(query))
            return "ERRORE DI SICUREZZA: La query contiene keyword non permesse (INSERT/UPDATE/DELETE/EXEC/DROP/...).";

        if (query.Contains("/*") || query.Contains("--"))
            return "ERRORE DI SICUREZZA: I commenti SQL non sono permessi.";

        if (MultiStatement.IsMatch(query))
            return "ERRORE DI SICUREZZA: Query multiple (;) e UNION non sono permessi.";

        if (SystemCatalog.IsMatch(query))
            return "ERRORE DI SICUREZZA: L'accesso ai cataloghi di sistema (sys.*, INFORMATION_SCHEMA.*) non è permesso.";

        return null;
    }

    /// <summary>
    /// Checks whether a given tool function name is subject to SQL validation.
    /// </summary>
    public static bool IsSqlTool(string functionName) => SqlToolNames.Contains(functionName);

    /// <summary>
    /// Checks whether a given parameter name is recognized as carrying a SQL query.
    /// </summary>
    public static bool IsSqlParameter(string parameterName) => SqlParameterNames.Contains(parameterName);

    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken) {

        // Early-exit: se il tool non è SQL, passa direttamente
        if (!SqlToolNames.Contains(context.Function.Name)) {
            return await next(context, cancellationToken);
        }

        // Cerca parametri SQL nel tool
        var sqlArg = context.Arguments
            .FirstOrDefault(a => SqlParameterNames.Contains(a.Key));

        if (sqlArg.Key is null) {
            return await next(context, cancellationToken);
        }

        var query = sqlArg.Value?.ToString();
        var error = ValidateSqlQuery(query);
        if (error != null) {
            _logger.LogWarning("[SQL SECURITY] Query bloccata da agent '{Agent}': {Error} | Query: {Query}",
                agent.Name, error, query);
            return error;
        }

        return await next(context, cancellationToken);
    }
}

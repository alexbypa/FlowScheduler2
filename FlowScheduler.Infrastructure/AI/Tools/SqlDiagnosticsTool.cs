using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace FlowScheduler.Infrastructure.AI.Tools;

/// <summary>
/// Diagnostica SQL avanzata: cerca blocchi attivi, deadlock e query pesanti recenti tramite DMV di sistema (sys.dm_exec_requests, sys.dm_exec_query_stats).
/// </summary>
public class SqlDiagnosticsTool {
    private readonly IDbConnectionFactoryResolver _dbFactoryResolver;
    private readonly string _connectionString;
    private readonly ILogger<SqlDiagnosticsTool> _logger;

    public SqlDiagnosticsTool(IDbConnectionFactoryResolver dbFactoryResolver, HangFireOptions hangFireOptions, ILogger<SqlDiagnosticsTool> logger) {
        _dbFactoryResolver = dbFactoryResolver;
        _connectionString = hangFireOptions.ConnectionString!;
        _logger = logger;
    }

    [Description("Esegue una diagnostica SQL profonda per individuare blocchi, query lente o deadlock su una specifica tabella o procedura.")]
    public async Task<string> GetSqlDiagnosticData(
        [Description("Il nome della tabella, stored procedure o parola chiave da cercare")] string tableName,
        [Description("Quanti minuti indietro cercare nello storico (default 30)")] int lookbackMinutes = 30,
        [Description("Il tipo di database (es. 'SqlServer', 'PostgreSql')")] string databaseType = "SqlServer") {

        _logger.LogInformation("[SQL DIAG TOOL] Avvio diagnostica per: {TableName} (lookback: {LookbackMinutes}m) su {DatabaseType}...", tableName, lookbackMinutes, databaseType);

        try {
            var dbFactory = _dbFactoryResolver.Resolve(databaseType);
            using var conn = dbFactory.CreateConnection(_connectionString);
            await conn.OpenAsync();

            string sqlDiagnostic = $@"
            SELECT 
                T.Type, 
                T.SessionId, 
                T.Status, 
                T.SourceObj,
                T.BlockingId, 
                T.Info, 
                T.Timestamp, 
                T.SqlText
            FROM (
                -- 1. REAL TIME LOCKS
                SELECT 
                    'REAL_TIME_BLOCK' as Type,
                    req.session_id as SessionId,
                    req.status as Status,
                    ISNULL(OBJECT_NAME(txt.objectid, txt.dbid), 'Ad-Hoc / Code') as SourceObj,
                    req.blocking_session_id as BlockingId,
                    'Wait: ' + req.wait_type + ' (' + CAST(req.wait_time AS VARCHAR) + 'ms)' as Info,
                    CONVERT(VARCHAR, GETDATE(), 120) as Timestamp, 
                    txt.text AS SqlText
                FROM sys.dm_exec_requests req
                CROSS APPLY sys.dm_exec_sql_text(req.sql_handle) txt
                WHERE req.blocking_session_id > 0 
                   OR txt.text LIKE '%' + @TableName + '%'

                UNION ALL

                -- 2. HISTORY (Query pesanti recenti)
                SELECT * FROM (
                    SELECT TOP 10
                        'HISTORY_HEAVY_QUERY' as Type,
                        0 as SessionId,
                        'Finished' as Status,
                        ISNULL(OBJECT_NAME(qt.objectid, qt.dbid), 'Ad-Hoc / Code') as SourceObj,
                        0 as BlockingId,
                        'AvgDuration: ' + CAST(CAST((qs.total_elapsed_time / qs.execution_count) / 1000 AS INT) AS VARCHAR) + 'ms' as Info,
                        CONVERT(VARCHAR, qs.last_execution_time, 120) as Timestamp,
                        SUBSTRING(qt.text, (qs.statement_start_offset/2)+1, 
                            ((CASE qs.statement_end_offset
                            WHEN -1 THEN DATALENGTH(qt.text)
                            ELSE qs.statement_end_offset
                            END - qs.statement_start_offset)/2) + 1) as SqlText
                    FROM sys.dm_exec_query_stats qs
                    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) qt
                    WHERE qs.last_execution_time >= DATEADD(minute, -@Lookback, GETDATE())
                    AND qt.text LIKE '%' + @TableName + '%'
                    AND qt.text NOT LIKE '%dm_exec_query_stats%' 
                ) AS HistoryDerived
            ) AS T
            FOR JSON PATH;";

            using var cmd = dbFactory.CreateCommand(sqlDiagnostic, conn);
            dbFactory.AddParameter(cmd, "@TableName", tableName);
            dbFactory.AddParameter(cmd, "@Lookback", lookbackMinutes);

            var result = await cmd.ExecuteScalarAsync();
            var json = result?.ToString() ?? "[]";

            if (json == "[]")
                return $"Nessun problema rilevato per '{tableName}' negli ultimi {lookbackMinutes} minuti.";

            return json;
        } catch (Exception ex) {
            return $"Errore durante la diagnostica SQL: {ex.Message}";
        }
    }
}
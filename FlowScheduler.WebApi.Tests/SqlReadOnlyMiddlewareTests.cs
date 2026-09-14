using FlowScheduler.Infrastructure.AI.Middleware;

namespace FlowScheduler.WebApi.Tests;

/// <summary>
/// Security tests for SqlReadOnlyMiddleware — validates that AI agents can ONLY execute SELECT queries.
/// Each blocked category covers a distinct SQL injection or mutation vector.
/// </summary>
public class SqlReadOnlyMiddlewareTests {

    // ─── SAFE SELECT QUERIES (must pass) ───────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM Users")]
    [InlineData("SELECT TOP 10 * FROM Logs WHERE Level = 'Error'")]
    [InlineData("SELECT COUNT(*) FROM Orders")]
    [InlineData("select lower_case from table1")]
    [InlineData("SELECT u.Name, o.Total FROM Users u JOIN Orders o ON u.Id = o.UserId")]
    [InlineData("SELECT DISTINCT Category FROM Products WHERE Price > 100")]
    [InlineData("SELECT * FROM (SELECT Id, Name FROM Users) AS sub")]
    [InlineData("  SELECT * FROM Users  ")] // leading/trailing whitespace
    public void ValidateSqlQuery_SafeSelect_ReturnsNull(string query) {
        Assert.Null(SqlReadOnlyMiddleware.ValidateSqlQuery(query));
    }

    // ─── BLOCK: DML mutations ──────────────────────────────────────────────

    [Theory]
    [InlineData("INSERT INTO Users VALUES (1, 'evil')")]
    [InlineData("UPDATE Users SET IsAdmin = 1")]
    [InlineData("DELETE FROM Users WHERE Id = 1")]
    [InlineData("MERGE INTO Target USING Source ON Target.Id = Source.Id WHEN MATCHED THEN UPDATE SET Name = 'x'")]
    [InlineData("TRUNCATE TABLE Users")]
    public void ValidateSqlQuery_DmlMutation_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: DDL operations ─────────────────────────────────────────────

    [Theory]
    [InlineData("DROP TABLE Users")]
    [InlineData("ALTER TABLE Users ADD Column1 INT")]
    [InlineData("CREATE TABLE Evil (Id INT)")]
    public void ValidateSqlQuery_DdlOperation_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: Dangerous keywords hidden inside SELECT ────────────────────

    [Theory]
    [InlineData("SELECT * FROM Users WHERE 1=1 EXEC('evil')")]
    [InlineData("SELECT * FROM Users WHERE 1=1 EXECUTE('evil')")]
    [InlineData("SELECT xp_cmdshell('dir') FROM dual")]
    [InlineData("SELECT sp_executesql('DROP TABLE Users') FROM dual")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=evil')")]
    [InlineData("SELECT * FROM OPENQUERY(LinkedServer, 'SELECT * FROM secrets')")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Server=evil')..Table1")]
    [InlineData("SELECT * FROM Users GRANT ALL TO public")]
    [InlineData("SELECT * FROM Users REVOKE SELECT ON Users FROM public")]
    [InlineData("SELECT * FROM Users DENY SELECT ON Users TO guest")]
    [InlineData("SELECT SHUTDOWN FROM dual")]
    [InlineData("SELECT * FROM Users DBCC CHECKDB")]
    [InlineData("SELECT * FROM Users BULK INSERT Users FROM 'file.csv'")]
    public void ValidateSqlQuery_DangerousKeywordInSelect_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: SQL injection via comments ─────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM Users /* hidden evil */")]
    [InlineData("SELECT * FROM Users -- rest is ignored")]
    [InlineData("SELECT * FROM Users /**/")]
    public void ValidateSqlQuery_SqlComments_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: Statement stacking and UNION injection ─────────────────────

    [Theory]
    [InlineData("SELECT 1; DROP TABLE Users")]
    [InlineData("SELECT * FROM Users; SELECT * FROM Passwords")]
    [InlineData("SELECT * FROM Users UNION SELECT * FROM Passwords")]
    [InlineData("SELECT * FROM Users UNION ALL SELECT * FROM Secrets")]
    public void ValidateSqlQuery_MultiStatementOrUnion_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: SELECT INTO (data exfiltration to temp tables/vars) ────────

    [Theory]
    [InlineData("SELECT * INTO #TempTable FROM Users")]
    [InlineData("SELECT * INTO @Variable FROM Users")]
    public void ValidateSqlQuery_SelectInto_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── BLOCK: System catalog / schema discovery ────────────────────────────
    // Prevents AI from discovering database schema via metadata tables

    [Theory]
    [InlineData("SELECT * FROM sys.tables")]
    [InlineData("SELECT * FROM sys.columns")]
    [InlineData("SELECT * FROM sys.objects WHERE type = 'U'")]
    [InlineData("SELECT * FROM sys.dm_exec_requests")]
    [InlineData("SELECT * FROM sys.dm_exec_query_stats")]
    [InlineData("SELECT * FROM sys.dm_exec_sessions")]
    [InlineData("SELECT * FROM sys.procedures")]
    [InlineData("SELECT * FROM sys.indexes")]
    [InlineData("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.ROUTINES")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE")]
    [InlineData("SELECT * FROM sysobjects WHERE xtype = 'U'")]
    [InlineData("SELECT * FROM syscolumns")]
    [InlineData("SELECT * FROM sysindexes")]
    [InlineData("SELECT * FROM sysprocesses")]
    [InlineData("select name from sys.tables")]        // lowercase evasion
    [InlineData("SELECT * FROM information_schema.tables")] // lowercase evasion
    public void ValidateSqlQuery_SystemCatalog_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("cataloghi di sistema", error);
    }

    // ─── BLOCK: Edge cases and evasion attempts ────────────────────────────

    [Theory]
    [InlineData(null)]        // null query
    [InlineData("")]          // empty string
    [InlineData("   ")]       // whitespace only
    [InlineData("SELEC * FROM Users")] // typo — not SELECT
    public void ValidateSqlQuery_EdgeCases_Blocked(string? query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    [Fact]
    public void ValidateSqlQuery_SelectPrefixTrick_PassesBecauseInvalidSqlFailsAtDb() {
        // "SELECTX" starts with SELECT → passes prefix check.
        // It's invalid SQL and will fail at DB execution — not a security risk.
        Assert.Null(SqlReadOnlyMiddleware.ValidateSqlQuery("SELECTX * FROM Users"));
    }

    // ─── BLOCK: Case evasion attempts ──────────────────────────────────────

    [Theory]
    [InlineData("insert INTO Users VALUES (1)")]
    [InlineData("Update Users SET x = 1")]
    [InlineData("dElEtE FROM Users")]
    [InlineData("DrOp TABLE Users")]
    public void ValidateSqlQuery_CaseEvasion_Blocked(string query) {
        var error = SqlReadOnlyMiddleware.ValidateSqlQuery(query);
        Assert.NotNull(error);
        Assert.Contains("ERRORE DI SICUREZZA", error);
    }

    // ─── Tool name coverage verification ───────────────────────────────────

    [Theory]
    [InlineData("ExecuteQueryAndStoreAsync")]  // SqlTool method
    [InlineData("GetSqlDiagnosticData")]       // SqlDiagnosticsTool method
    [InlineData("GetDatabaseErrors")]          // DatabaseTool method
    public void IsSqlTool_RegisteredToolMethods_ReturnsTrue(string toolName) {
        Assert.True(SqlReadOnlyMiddleware.IsSqlTool(toolName));
    }

    [Theory]
    [InlineData("SearchKnowledgeBaseAsync")]  // RagTool — no SQL
    [InlineData("ReadGitHubCode")]            // GitHubTool — no SQL
    [InlineData("GetLogDetails")]             // DiagnosticTool — no SQL
    [InlineData("RandomToolName")]
    public void IsSqlTool_NonSqlTools_ReturnsFalse(string toolName) {
        Assert.False(SqlReadOnlyMiddleware.IsSqlTool(toolName));
    }

    // ─── SQL parameter name coverage ───────────────────────────────────────

    [Theory]
    [InlineData("sqlQuery")]   // SqlTool.ExecuteQueryAndStoreAsync parameter
    [InlineData("sql")]        // Generic SQL parameter
    public void IsSqlParameter_KnownParameters_ReturnsTrue(string paramName) {
        Assert.True(SqlReadOnlyMiddleware.IsSqlParameter(paramName));
    }

    [Theory]
    [InlineData("tableName")]     // SqlDiagnosticsTool — not arbitrary SQL
    [InlineData("query")]         // Not registered
    [InlineData("minutes")]       // DatabaseTool numeric param
    [InlineData("databaseType")]  // Type selector
    public void IsSqlParameter_NonSqlParameters_ReturnsFalse(string paramName) {
        Assert.False(SqlReadOnlyMiddleware.IsSqlParameter(paramName));
    }
}

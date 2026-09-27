using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.Data;
using FlowScheduler.Infrastructure.AI.Tools;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Data;
using System.Data.Common;

namespace FlowScheduler.WebApi.Tests.AI.Tools;

public class SqlDiagnosticsToolTests {

    // ─── SqlDiagnosticsTool ────────────────────────────────────────────────

    [Fact]
    public async Task GetSqlDiagnosticData_WithResults_ReturnsJson() {
        var expectedJson = "[{\"Type\":\"REAL_TIME_BLOCK\",\"SessionId\":55}]";
        var (resolver, factory, conn, cmd) = CreateDbMocks();
        cmd.ExecuteScalarAsync(Arg.Any<CancellationToken>()).Returns(expectedJson);

        var sut = new SqlDiagnosticsTool(resolver, CreateOptions(), CreateLogger<SqlDiagnosticsTool>());
        var result = await sut.GetSqlDiagnosticData("MyTable", 30, "SqlServer");

        Assert.Equal(expectedJson, result);
    }

    [Fact]
    public async Task GetSqlDiagnosticData_NoResults_ReturnsNoProblemsMessage() {
        var (resolver, factory, conn, cmd) = CreateDbMocks();
        cmd.ExecuteScalarAsync(Arg.Any<CancellationToken>()).Returns((object?)null);

        var sut = new SqlDiagnosticsTool(resolver, CreateOptions(), CreateLogger<SqlDiagnosticsTool>());
        var result = await sut.GetSqlDiagnosticData("MyTable", 30, "SqlServer");

        Assert.Contains("Nessun problema rilevato", result);
    }

    [Fact]
    public async Task GetSqlDiagnosticData_EmptyJsonArray_ReturnsNoProblemsMessage() {
        var (resolver, factory, conn, cmd) = CreateDbMocks();
        cmd.ExecuteScalarAsync(Arg.Any<CancellationToken>()).Returns("[]");

        var sut = new SqlDiagnosticsTool(resolver, CreateOptions(), CreateLogger<SqlDiagnosticsTool>());
        var result = await sut.GetSqlDiagnosticData("Orders", 60, "SqlServer");

        Assert.Contains("Nessun problema rilevato", result);
        Assert.Contains("Orders", result);
    }

    [Fact]
    public async Task GetSqlDiagnosticData_ConnectionError_ReturnsErrorMessage() {
        var resolver = Substitute.For<IDbConnectionFactoryResolver>();
        resolver.Resolve(Arg.Any<string>()).Throws(new InvalidOperationException("Connection refused"));

        var sut = new SqlDiagnosticsTool(resolver, CreateOptions(), CreateLogger<SqlDiagnosticsTool>());
        var result = await sut.GetSqlDiagnosticData("MyTable");

        Assert.Contains("Errore durante la diagnostica SQL", result);
        Assert.Contains("Connection refused", result);
    }

    // ─── DatabaseTool ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetDatabaseErrors_WithErrors_ReturnsReport() {
        var (resolver, factory, conn, cmd) = CreateDbMocks();
        var reader = CreateMockReader(new[] {
            (new DateTime(2026, 9, 27, 14, 30, 0), "NullReferenceException in Handler", "Error"),
            (new DateTime(2026, 9, 27, 14, 31, 5), "Timeout expired", "Fatal"),
        });
        cmd.ExecuteReaderAsync(Arg.Any<CancellationToken>()).Returns(reader);

        var sut = new DatabaseTool(resolver, CreateOptions(), CreateLogger<DatabaseTool>());
        var result = await sut.GetDatabaseErrors(60, "SqlServer");

        Assert.Contains("REPORT ERRORI REALI", result);
        Assert.Contains("14:30:00", result);
        Assert.Contains("NullReferenceException in Handler", result);
        Assert.Contains("Timeout expired", result);
    }

    [Fact]
    public async Task GetDatabaseErrors_NoErrors_ReturnsNoErrorMessage() {
        var (resolver, factory, conn, cmd) = CreateDbMocks();
        var reader = CreateMockReader(Array.Empty<(DateTime, string, string)>());
        cmd.ExecuteReaderAsync(Arg.Any<CancellationToken>()).Returns(reader);

        var sut = new DatabaseTool(resolver, CreateOptions(), CreateLogger<DatabaseTool>());
        var result = await sut.GetDatabaseErrors(30, "SqlServer");

        Assert.Contains("Nessun errore trovato", result);
    }

    [Fact]
    public async Task GetDatabaseErrors_ConnectionError_ReturnsErrorMessage() {
        var resolver = Substitute.For<IDbConnectionFactoryResolver>();
        resolver.Resolve(Arg.Any<string>()).Throws(new InvalidOperationException("Server unreachable"));

        var sut = new DatabaseTool(resolver, CreateOptions(), CreateLogger<DatabaseTool>());
        var result = await sut.GetDatabaseErrors(60);

        Assert.Contains("Errore durante l'interrogazione del database", result);
        Assert.Contains("Server unreachable", result);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static (IDbConnectionFactoryResolver resolver, IDbConnectionFactory factory, DbConnection conn, DbCommand cmd) CreateDbMocks() {
        var resolver = Substitute.For<IDbConnectionFactoryResolver>();
        var factory = Substitute.For<IDbConnectionFactory>();
        var conn = Substitute.For<DbConnection>();
        var cmd = Substitute.For<DbCommand>();

        resolver.Resolve(Arg.Any<string>()).Returns(factory);
        factory.CreateConnection(Arg.Any<string>()).Returns(conn);
        factory.CreateCommand(Arg.Any<string>(), Arg.Any<DbConnection>()).Returns(cmd);

        return (resolver, factory, conn, cmd);
    }

    private static DbDataReader CreateMockReader((DateTime timestamp, string message, string level)[] rows) {
        var reader = Substitute.For<DbDataReader>();
        var index = -1;

        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ => {
            index++;
            return index < rows.Length;
        });

        reader.GetDateTime(0).Returns(ci => rows[index].timestamp);
        reader.GetString(1).Returns(ci => rows[index].message);
        reader.GetString(2).Returns(ci => rows[index].level);

        return reader;
    }

    private static HangFireOptions CreateOptions() => new() { ConnectionString = "Server=test;Database=test;" };

    private static ILogger<T> CreateLogger<T>() => Substitute.For<ILogger<T>>();
}

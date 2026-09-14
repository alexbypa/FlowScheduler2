using FlowScheduler.Core.Interfaces.Data;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlowScheduler.Infrastructure.Database;

public class PostgreSqlConnectionFactory : IDbConnectionFactory {
    public bool SupportsStoredProcedures => true;
    public void AddParameter(DbCommand command, string name, object value) => command.Parameters.Add(new NpgsqlParameter(name, value));
    public DbCommand CreateCommand(string commandText, DbConnection connection) {
        var cmd = new NpgsqlCommand(commandText, (NpgsqlConnection)connection);
        cmd.CommandType = CommandType.StoredProcedure;
        return cmd;
    }
    public DbConnection CreateConnection(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new NpgsqlConnection(builder.ConnectionString);
    }
}
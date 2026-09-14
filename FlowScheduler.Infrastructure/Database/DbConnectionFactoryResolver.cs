using FlowScheduler.Core.Interfaces.Data;

namespace FlowScheduler.Infrastructure.Database;

public class DbConnectionFactoryResolver : IDbConnectionFactoryResolver {
    private readonly IEnumerable<IDbConnectionFactory> _factories;

    public DbConnectionFactoryResolver(IEnumerable<IDbConnectionFactory> factories) {
        _factories = factories;
    }

    public IDbConnectionFactory Resolve(string databaseType) {
        var typeName = $"{databaseType}ConnectionFactory";
        var factory = _factories.FirstOrDefault(f =>
            f.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

        if (factory == null)
            throw new NotSupportedException($"Database type '{databaseType}' non supportato.");

        return factory;
    }
}

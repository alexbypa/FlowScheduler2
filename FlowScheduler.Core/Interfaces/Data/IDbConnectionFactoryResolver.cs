namespace FlowScheduler.Core.Interfaces.Data;

public interface IDbConnectionFactoryResolver {
    IDbConnectionFactory Resolve(string databaseType);
}

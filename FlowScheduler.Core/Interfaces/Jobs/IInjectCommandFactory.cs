namespace FlowScheduler.Core.Interfaces.Jobs;

public interface IInjectCommandFactory {
    IJobCommand GetCommandByName(string commandName);
}

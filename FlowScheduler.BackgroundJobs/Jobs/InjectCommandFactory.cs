using FlowScheduler.Core.Interfaces.Jobs;

namespace FlowScheduler.BackgroundJobs.Jobs;

public class InjectCommandFactory : IInjectCommandFactory {
    private readonly IEnumerable<IJobCommand> _servicesJob;
    public InjectCommandFactory(IEnumerable<IJobCommand> servicesJob) {
        _servicesJob = servicesJob;
    }
    public IJobCommand GetCommandByName(string commandName) {
        var command = _servicesJob.FirstOrDefault(c => c.GetType().Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
        if (command == null) {
            throw new Exception($"Comando '{commandName}' non supportato o non registrato.");
        }
        return command;
    }
}

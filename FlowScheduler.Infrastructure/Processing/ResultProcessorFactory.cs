using FlowScheduler.Core.Interfaces.Processing;

namespace FlowScheduler.Infrastructure.Processing;

public class ResultProcessorFactory : IResultProcessorFactory {
    private readonly IEnumerable<IResultProcessor> _processors;
    public ResultProcessorFactory(IEnumerable<IResultProcessor> processors) {
        _processors = processors;
    }
    public IResultProcessor GetProcessor(string processorType) {
        return _processors.FirstOrDefault(p =>
              p.GetType().Name.Equals(processorType, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Processor '{processorType}' non registrato.");
    }
}

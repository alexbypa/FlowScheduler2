namespace FlowScheduler.Core.Interfaces.Processing;

public interface IResultProcessorFactory {
    IResultProcessor GetProcessor(string processorType);
}

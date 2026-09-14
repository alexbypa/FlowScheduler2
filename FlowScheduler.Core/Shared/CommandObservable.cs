namespace FlowScheduler.Core.Shared;

public enum LogLevel : byte {
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4
}

public class WriteTextOnDashboard {
    public WriteTextOnDashboard(string text) { Text = text; }
    public string Text { get; }
}

public delegate void WriteTextOnDashboardHandler(LogLevel levelLog, WriteTextOnDashboard e);

public interface ICommandObservable {
    event WriteTextOnDashboardHandler OnWriteText;
}

public class CommandObservable : ICommandObservable {
    public event WriteTextOnDashboardHandler? OnWriteText;
    protected virtual void RaiseMessage(LogLevel levelLog, string message) {
        OnWriteText?.Invoke(levelLog, new WriteTextOnDashboard(message));
    }
}

using System.Threading;
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Messaging;

public interface ITelegramService {
    Task SendDiagnosticAsync(DiagnosticResult result, string taskName, string jobName, CancellationToken cancellationToken = default);
    Task SendMessageAsync(string chatId, string text, CancellationToken cancellationToken = default);
    Task<int> SendMessageWithIdAsync(string chatId, string text, CancellationToken cancellationToken = default);
    Task UpdateMessageAsync(string chatId, int messageId, string text, CancellationToken cancellationToken = default);
    Task SendWithInlineKeyboardAsync(string chatId, string text, Dictionary<string, string> buttons, CancellationToken cancellationToken = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using Telegram.Bot.Types;
using TgLlmBot.DataAccess.Models;
using TgLlmBot.Models;
using TgLlmBot.Services.DataAccess.Limits;
using TgLlmBot.Services.DataAccess.SystemPrompts;
using TgLlmBot.Services.DataAccess.TelegramMessages;
using TgLlmBot.Services.Llm;
using TgLlmBot.Services.Mcp.Tools;
using TgLlmBot.Services.Media;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

// In-memory dependencies for a text-only conversation. Unexpected media and configuration writes fail immediately.
internal sealed class ChatHandlerTestServices : ITelegramMessageStorage, ISystemPromptService, IMcpToolsProvider, IMediaPreparer, IMediaGroupTracker, ILlmLimitsService
{
    public List<string?> StoredTexts { get; } = [];

    public bool FailStore { get; init; }

    public Task<DbChatMessage> StoreMessageAsync(Message message, User self, CancellationToken cancellationToken)
    {
        return StoreAsync(message.Text);
    }

    public Task<DbChatMessage> StoreMessageAsync(Message message, User self, AppliedCustomPrompt customPrompt, CancellationToken cancellationToken)
    {
        return StoreAsync(message.Text);
    }

    public Task<DbChatMessage> StoreMessageAsync(Message message, User self, AppliedCustomPrompt customPrompt, string text, CancellationToken cancellationToken)
    {
        return StoreAsync(text);
    }

    private Task<DbChatMessage> StoreAsync(string? text)
    {
        if (FailStore)
        {
            throw new InvalidOperationException("Storage is unavailable");
        }

        StoredTexts.Add(text);
        return Task.FromResult(new DbChatMessage());
    }

    public Task<DbChatMessage[]> SelectContextMessagesAsync(Message message, CancellationToken cancellationToken) => Task.FromResult<DbChatMessage[]>([]);

    public Task<DbChatMessage?> SelectMessageAsync(long chatId, int messageId, CancellationToken cancellationToken) => Task.FromResult<DbChatMessage?>(null);

    public Task<DbChatMessage[]> SelectContextMessagesBeforeAsync(long chatId, int messageId, DateTime date, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DbChatMessage[]> SelectMessagesAsync(long chatId, IReadOnlyCollection<int> messageIds, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DbChatMessage[]> SelectMediaGroupMessagesAsync(long chatId, string mediaGroupId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DbChatMessage[]> SelectMessagesWithUnfinishedMediaAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task UpdateMediaAsync(DbChatMessageMedia[] media, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<string>> GetChatPromptAsync(long chatId, CancellationToken cancellationToken) => Task.FromResult(Result<string>.Fail());

    public Task<Result<string>> GetUserChatPromptAsync(long chatId, long userId, CancellationToken cancellationToken) => Task.FromResult(Result<string>.Fail());

    public Task SetChatPromptAsync(long chatId, string? systemPrompt, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResetChatPromptAsync(long chatId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetUserChatPromptAsync(long chatId, long userId, string? systemPrompt, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResetUserChatPromptAsync(long chatId, long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public IReadOnlyCollection<McpClientTool> GetTools() => [];

    public Task<Result<PreparedMedia>> PrepareAsync(DbChatMessageMedia media, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Register(long chatId, string mediaGroupId) => throw new NotSupportedException();

    public Task WaitForSettleAsync(long chatId, string mediaGroupId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool TryBeginLlmRequest(long chatId, string mediaGroupId) => throw new NotSupportedException();

    public Task<bool> IsLLmInteractionAllowedAsync(long chatId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task IncrementUsageAsync(long chatId, long userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetDailyLimitsAsync(long chatId, long userId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetChatDailyLimitAsync(long chatId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
}

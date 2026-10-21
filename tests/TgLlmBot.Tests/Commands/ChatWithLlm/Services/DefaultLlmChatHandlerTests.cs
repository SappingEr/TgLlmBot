using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgLlmBot.Commands.ChatWithLlm;
using TgLlmBot.Commands.ChatWithLlm.Services;
using TgLlmBot.Configuration.TypedConfiguration.Llm;
using TgLlmBot.Services.Telegram.TypingStatus;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmChatHandlerTests
{
    private const long ChatId = -100123;
    private const string Markdown = "**ответ** [ссылка](https://example.com)";
    private const string HistoryText = "ответ ссылка (https://example.com)";

    [Fact]
    public async Task HandleCommandAsync_FormattingError_RetriesTypedBlocksAndStoresHistoryOnce()
    {
        using var api = new StubTelegramApi(400, "Bad Request: can't parse entities");
        using var http = new HttpClient(api);
        using var chat = new StubChatClient();
        var services = new ChatHandlerTestServices();
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["sendRichMessage", "sendRichMessage"], api.Methods);
        var original = api.Bodies[0];
        var fallback = api.Bodies[1];
        Assert.Contains("<details>", original.GetProperty("rich_message").GetProperty("html").GetString(), StringComparison.Ordinal);
        var richMessage = fallback.GetProperty("rich_message");
        Assert.False(richMessage.TryGetProperty("html", out _));
        var details = Assert.Single(richMessage.GetProperty("blocks").EnumerateArray());
        Assert.Equal("details", details.GetProperty("type").GetString());
        Assert.Equal("Развернуть ответ", details.GetProperty("summary").GetString());
        var paragraph = Assert.Single(details.GetProperty("blocks").EnumerateArray());
        Assert.Equal("paragraph", paragraph.GetProperty("type").GetString());
        Assert.Equal(HistoryText, paragraph.GetProperty("text").GetString());
        Assert.All(api.Bodies, static body =>
        {
            Assert.Equal(ChatId, body.GetProperty("chat_id").GetInt64());
            Assert.Equal(17, body.GetProperty("message_thread_id").GetInt32());
            Assert.Equal(42, body.GetProperty("reply_parameters").GetProperty("message_id").GetInt32());
        });
        Assert.Equal([HistoryText], services.StoredTexts);
        Assert.Equal(1, chat.Calls);
    }

    [Theory]
    [InlineData(400, "Bad Request: chat not found")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error: parse failed")]
    public async Task HandleCommandAsync_OtherApiError_DoesNotRetryRichMessage(int errorCode, string description)
    {
        using var api = new StubTelegramApi(errorCode, description);
        using var http = new HttpClient(api);
        using var chat = new StubChatClient();
        var services = new ChatHandlerTestServices();
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["sendRichMessage", "sendMessage"], api.Methods);
        Assert.Equal(description, Assert.Single(services.StoredTexts));
        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task HandleCommandAsync_StorageFailureAfterSuccessfulSend_DoesNotResendResponse()
    {
        using var api = new StubTelegramApi();
        using var http = new HttpClient(api);
        using var chat = new StubChatClient();
        var services = new ChatHandlerTestServices { FailStore = true };
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["sendRichMessage"], api.Methods);
        Assert.Empty(services.StoredTexts);
        Assert.Equal(1, chat.Calls);
    }

    private static DefaultLlmChatHandler CreateHandler(HttpClient http, StubChatClient chat, ChatHandlerTestServices services)
    {
        var bot = new TelegramBotClient(new TelegramBotClientOptions("123456:TEST_TOKEN") { RetryCount = 0 }, http);
        var queues = new DefaultTypingStatusQueues(new(new HashSet<long> { ChatId }));
        var typing = new TypingStatusService(queues, NullLogger<TypingStatusService>.Instance);
        return new(new("test bot", "empty"), TimeProvider.System, bot, chat, LlmCapabilitiesConfiguration.Convert(new()),
            services, services, services, FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: 1),
            services, services, typing, services, NullLogger<DefaultLlmChatHandler>.Instance);
    }

    private static ChatWithLlmCommand CreateCommand()
    {
        return new(new()
        {
            Id = 42,
            Chat = new() { Id = ChatId, Type = ChatType.Supergroup },
            MessageThreadId = 17,
            Date = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc),
            From = new() { Id = 7, FirstName = "User" },
            Text = "вопрос"
        }, UpdateType.Message, new() { Id = 123456, IsBot = true, FirstName = "Bot" }, null);
    }

    private sealed class StubChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Markdown)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubTelegramApi(int? errorCode = null, string? description = null) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        public List<JsonElement> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.RequestUri);
            Assert.NotNull(request.Content);
            Methods.Add(request.RequestUri.Segments[^1]);
            using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var body = document.RootElement.Clone();
            Bodies.Add(body);
            var failed = Bodies.Count == 1 && errorCode.HasValue;
            var json = failed
                ? JsonSerializer.Serialize(new { ok = false, error_code = errorCode, description })
                : JsonSerializer.Serialize(new
                {
                    ok = true,
                    result = new { message_id = 99, date = 1792584000, chat = new { id = ChatId, type = "supergroup" }, text = body.TryGetProperty("text", out var text) ? text.GetString() : null }
                });
            return new(failed ? (HttpStatusCode) errorCode!.Value : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

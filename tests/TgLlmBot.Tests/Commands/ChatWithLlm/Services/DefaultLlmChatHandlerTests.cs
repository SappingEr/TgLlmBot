using System;
using System.Collections.Generic;
using System.Linq;
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
using TgLlmBot.DataAccess.Models;
using TgLlmBot.Services.Telegram.TypingStatus;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmChatHandlerTests
{
    private const long ChatId = -100123;
    private const string Markdown = "**ответ** [ссылка](https://example.com)";
    private const string HistoryText = "ответ ссылка (https://example.com)";

    [Theory]
    [InlineData("Bad Request: can't parse entities")]
    [InlineData("Bad Request: invalid rich message")]
    [InlineData("Bad Request: RICH_MESSAGE_INVALID")]
    [InlineData("Bad Request: unsupported tag 'example'")]
    [InlineData("Bad Request: Unsupported start tag 'example'")]
    public async Task HandleCommandAsync_FormattingError_RetriesTypedBlocksAndStoresHistoryOnce(string description)
    {
        using var api = new StubTelegramApi(400, description);
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
    [InlineData(400, "Bad Request: entity not found")]
    [InlineData(400, "Bad Request: failed to parse chat_id")]
    [InlineData(400, "Bad Request: sender identity is invalid")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error: can't parse rich message")]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleCommandAsync_PartialResponseFailure_SendsNoticeWithoutStoringOrResendingIt(bool storageFailure)
    {
        using var api = storageFailure ? new StubTelegramApi() : new StubTelegramApi(500, "Internal Server Error", failedRequest: 2);
        using var http = new HttpClient(api);
        using var chat = new StubChatClient(new string('a', 5000));
        var services = new ChatHandlerTestServices { FailStore = storageFailure };
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        string[] expectedMethods = storageFailure ? ["sendRichMessage", "sendMessage"] : ["sendRichMessage", "sendRichMessage", "sendMessage"];
        Assert.Equal(expectedMethods, api.Methods);
        var notice = api.Bodies[^1];
        Assert.Equal("⚠️ Не удалось отправить ответ полностью. Повторите запрос.", notice.GetProperty("text").GetString());
        Assert.Equal(ChatId, notice.GetProperty("chat_id").GetInt64());
        Assert.Equal(17, notice.GetProperty("message_thread_id").GetInt32());
        Assert.Equal(42, notice.GetProperty("reply_parameters").GetProperty("message_id").GetInt32());
        Assert.Equal(1, services.StoreAttempts);
        if (storageFailure)
        {
            Assert.Empty(services.StoredTexts);
        }
        else
        {
            Assert.Equal(new string('a', 4096), Assert.Single(services.StoredTexts));
        }

        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task HandleCommandAsync_StorageFailureAfterFinalPart_DoesNotSendInterruptionNotice()
    {
        using var api = new StubTelegramApi();
        using var http = new HttpClient(api);
        using var chat = new StubChatClient(new string('a', 5000));
        var services = new ChatHandlerTestServices { FailStoreOnAttempt = 2 };
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["sendRichMessage", "sendRichMessage"], api.Methods);
        Assert.Equal(2, services.StoreAttempts);
        Assert.Equal(new string('a', 4096), Assert.Single(services.StoredTexts));
    }

    [Fact]
    public async Task HandleCommandAsync_InterruptionNoticeFails_DoesNotRetryResponseOrStoreNotice()
    {
        using var api = new StubTelegramApi(403, "Forbidden", failedRequest: 2);
        api.Errors.Add(3, (403, "Forbidden"));
        using var http = new HttpClient(api);
        using var chat = new StubChatClient(new string('a', 5000));
        var services = new ChatHandlerTestServices();
        var handler = CreateHandler(http, chat, services);

        await handler.HandleCommandAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(["sendRichMessage", "sendRichMessage", "sendMessage"], api.Methods);
        Assert.Equal(1, services.StoreAttempts);
        Assert.Equal(new string('a', 4096), Assert.Single(services.StoredTexts));
        Assert.Equal(1, chat.Calls);
    }

    [Theory]
    [InlineData(null, null, "", null, "Заголовок rich reply (https://example.com)")]
    [InlineData(null, null, " \t", "\n ", "Заголовок rich reply (https://example.com)")]
    [InlineData(" \t", null, null, null, "Заголовок rich reply (https://example.com)")]
    [InlineData(" \t", " stored text ", "text reply", "caption reply", "stored text")]
    [InlineData(null, null, " ", " caption reply ", "caption reply")]
    [InlineData(null, null, " text reply ", "caption reply", "text reply")]
    [InlineData(" stored reply ", null, "text reply", "caption reply", "stored reply")]
    [InlineData(null, " saved link (https://saved.test) ", null, null, "saved link (https://saved.test)")]
    public async Task HandleCommandAsync_ReplyWithEmptyFields_UsesFirstNonWhitespaceText(string? storedCaption, string? storedText, string? text, string? caption, string expected)
    {
        using var api = new StubTelegramApi();
        using var http = new HttpClient(api);
        using var chat = new StubChatClient();
        var services = new ChatHandlerTestServices
        {
            RepliedMessage = storedCaption is null && storedText is null ? null : new DbChatMessage { MessageId = 11, Caption = storedCaption, Text = storedText }
        };
        var handler = CreateHandler(http, chat, services);
        var command = CreateCommand();
        command.Message.ReplyToMessage = new()
        {
            Id = 11,
            Chat = command.Message.Chat,
            From = command.Self,
            Text = text,
            Caption = caption,
            RichMessage = new()
            {
                Blocks =
                [
                    new RichBlockDetails
                    {
                        Summary = new RichTextText { Text = "Заголовок" },
                        Blocks = [new RichBlockParagraph { Text = new RichTextUrl { Text = new RichTextText { Text = "rich reply" }, Url = "https://example.com" } }]
                    }
                ]
            }
        };

        await handler.HandleCommandAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains($"↳ в ответ на твоё сообщение #11: «{expected}»", chat.UserMessage, StringComparison.Ordinal);
        Assert.Equal(["sendRichMessage"], api.Methods);
        Assert.Equal(HistoryText, Assert.Single(services.StoredTexts));
    }

    [Theory]
    [InlineData(true, "Отчёт за неделю", "Отчёт за неделю Содержимое")]
    [InlineData(false, "Отчёт за неделю", "Отчёт за неделю Содержимое")]
    [InlineData(true, "", "Содержимое")]
    [InlineData(true, "Развернуть ответ", "Содержимое")]
    [InlineData(true, "Развернуть ответ · 1/2", "Содержимое")]
    [InlineData(true, "Развернуть ответ · 2/2", "Содержимое")]
    [InlineData(false, "Развернуть ответ", "Развернуть ответ Содержимое")]
    [InlineData(false, "Развернуть ответ · 1/2", "Развернуть ответ · 1/2 Содержимое")]
    [InlineData(true, "Развернуть ответ: пояснения", "Развернуть ответ: пояснения Содержимое")]
    [InlineData(true, "Развернуть ответ · 0/2", "Развернуть ответ · 0/2 Содержимое")]
    [InlineData(true, "Развернуть ответ · 3/2", "Развернуть ответ · 3/2 Содержимое")]
    [InlineData(true, "Развернуть ответ · 1/1", "Развернуть ответ · 1/1 Содержимое")]
    [InlineData(true, "Развернуть ответ · 1/2 extra", "Развернуть ответ · 1/2 extra Содержимое")]
    public async Task HandleCommandAsync_ReplyRichDetails_PreservesSummaryExceptOwnExpansionWrapper(bool fromSelf, string summary, string expected)
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockDetails
                {
                    Summary = new RichTextText { Text = summary },
                    Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "Содержимое" } }]
                }
            ]
        }, expected, fromSelf);
    }

    [Fact]
    public async Task HandleCommandAsync_ReplyRichDetails_StoredBotMarkerDoesNotHideAnotherSendersSummary()
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockDetails
                {
                    Summary = new RichTextText { Text = "Развернуть ответ" },
                    Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "Содержимое" } }]
                }
            ]
        }, "Развернуть ответ Содержимое", fromSelf: false, storedMessage: new() { MessageId = 11, IsLlmReplyToMessage = true });
    }

    [Fact]
    public async Task HandleCommandAsync_ReplyRichDetails_OnlyHidesGeneratedOuterSummary()
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockDetails
                {
                    Summary = new RichTextText { Text = "Развернуть ответ" },
                    Blocks =
                    [
                        new RichBlockDetails
                        {
                            Summary = new RichTextText { Text = "Развернуть ответ" },
                            Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "Вложенное содержимое" } }]
                        },
                        new RichBlockDetails
                        {
                            Summary = new RichTextText { Text = "Пояснения" },
                            Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "Детали" } }]
                        }
                    ]
                }
            ]
        }, "Развернуть ответ Вложенное содержимое Пояснения Детали");
    }

    [Fact]
    public async Task HandleCommandAsync_ReplyRichDetails_WithSiblingBlockPreservesSummary()
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockParagraph { Text = new RichTextText { Text = "Введение" } },
                new RichBlockDetails
                {
                    Summary = new RichTextText { Text = "Развернуть ответ" },
                    Blocks = [new RichBlockParagraph { Text = new RichTextText { Text = "Содержимое" } }]
                }
            ]
        }, "Введение Развернуть ответ Содержимое");
    }

    [Fact]
    public async Task HandleCommandAsync_ReplyRichDetails_WithEmptyBodyPreservesSummary()
    {
        await AssertRichReplyAsync(new()
        {
            Blocks = [new RichBlockDetails { Summary = new RichTextText { Text = "Вопрос без пояснений" }, Blocks = [] }]
        }, "Вопрос без пояснений", fromSelf: false);
    }

    [Theory]
    [InlineData("сайт", "https://example.com", "сайт (https://example.com)")]
    [InlineData("https://example.com", "https://example.com", "https://example.com")]
    [InlineData("", "https://example.com", "https://example.com")]
    [InlineData(" \t", "https://example.com", "https://example.com")]
    [InlineData("сайт", "", "сайт")]
    [InlineData("документы", "https://example.com?q=1&lang=ru#part", "документы (https://example.com?q=1&lang=ru#part)")]
    public async Task HandleCommandAsync_ReplyRichLink_PreservesDestinationWithoutDuplicatingIt(string label, string url, string expected)
    {
        await AssertRichReplyAsync(new()
        {
            Blocks = [new RichBlockParagraph { Text = new RichTextUrl { Text = new RichTextText { Text = label }, Url = url } }]
        }, expected);
    }

    [Fact]
    public async Task HandleCommandAsync_ReplyRichLink_WithFormattedLabelPreservesDestination()
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockParagraph
                {
                    Text = new RichTextBold
                    {
                        Text = new RichTextArray
                        {
                            Array =
                            [
                                new RichTextText { Text = "См. " },
                                new RichTextItalic
                                {
                                    Text = new RichTextUrl
                                    {
                                        Text = new RichTextArray
                                        {
                                            Array = [new RichTextText { Text = "доку" }, new RichTextCode { Text = new RichTextText { Text = "ментацию" } }]
                                        },
                                        Url = "https://example.com"
                                    }
                                }
                            ]
                        }
                    }
                }
            ]
        }, "См. документацию (https://example.com)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleCommandAsync_ReplyRichDetails_PreservesLinksInSummaryAndNestedBlocks(bool fromSelf)
    {
        await AssertRichReplyAsync(new()
        {
            Blocks =
            [
                new RichBlockDetails
                {
                    Summary = new RichTextUrl { Text = new RichTextText { Text = "Развернуть ответ" }, Url = "https://a.test" },
                    Blocks =
                    [
                        new RichBlockBlockQuotation
                        {
                            Blocks =
                            [
                                new RichBlockList
                                {
                                    Items =
                                    [
                                        new RichBlockListItem
                                        {
                                            Label = "1.",
                                            Blocks = [new RichBlockParagraph { Text = new RichTextUrl { Text = new RichTextText { Text = "сайт" }, Url = "https://b.test" } }]
                                        }
                                    ]
                                },
                                new RichBlockTable
                                {
                                    Cells = [[new RichBlockTableCell { Text = new RichTextUrl { Text = new RichTextText { Text = "документы" }, Url = "https://c.test" } }]]
                                }
                            ]
                        }
                    ]
                }
            ]
        }, "Развернуть ответ (https://a.test) сайт (https://b.test) документы (https://c.test)", fromSelf);
    }

    private static async Task AssertRichReplyAsync(RichMessage richMessage, string expected, bool fromSelf = true, DbChatMessage? storedMessage = null)
    {
        using var api = new StubTelegramApi();
        using var http = new HttpClient(api);
        using var chat = new StubChatClient();
        var services = new ChatHandlerTestServices { RepliedMessage = storedMessage };
        var handler = CreateHandler(http, chat, services);
        var command = CreateCommand();
        command.Message.ReplyToMessage = new()
        {
            Id = 11,
            Chat = command.Message.Chat,
            From = fromSelf ? command.Self : new() { Id = 9, IsBot = true, FirstName = "Other bot" },
            RichMessage = richMessage
        };

        await handler.HandleCommandAsync(command, TestContext.Current.CancellationToken);

        Assert.Contains($": «{expected}»", chat.UserMessage, StringComparison.Ordinal);
        Assert.Equal(["sendRichMessage"], api.Methods);
        Assert.Equal(HistoryText, Assert.Single(services.StoredTexts));
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

    private sealed class StubChatClient(string response = Markdown) : IChatClient
    {
        public int Calls { get; private set; }

        public string? UserMessage { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            UserMessage = messages.Last(static message => message.Role == ChatRole.User).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubTelegramApi : HttpMessageHandler
    {
        public StubTelegramApi(int? errorCode = null, string? description = null, int failedRequest = 1)
        {
            if (errorCode is { } code)
            {
                Errors.Add(failedRequest, (code, description ?? "API error"));
            }
        }

        public Dictionary<int, (int Code, string Description)> Errors { get; } = [];

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
            var failed = Errors.TryGetValue(Bodies.Count, out var error);
            var json = failed
                ? JsonSerializer.Serialize(new { ok = false, error_code = error.Code, description = error.Description })
                : JsonSerializer.Serialize(new
                {
                    ok = true,
                    result = new { message_id = 99, date = 1792584000, chat = new { id = ChatId, type = "supergroup" }, text = body.TryGetProperty("text", out var text) ? text.GetString() : null }
                });
            return new(failed ? (HttpStatusCode) error.Code : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

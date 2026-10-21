using System;
using Telegram.Bot.Types;
using TgLlmBot.Commands.ChatWithLlm.Services;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class LlmResponsePartTests
{
    [Fact]
    public void CreateMessage_NoSummary_ReturnsOriginalHtml()
    {
        var part = new LlmResponsePart("ответ", "<p><b>ответ</b></p>", null);

        var message = part.CreateMessage();

        Assert.Equal("ответ", part.Text);
        Assert.Equal("<p><b>ответ</b></p>", message.Html);
        Assert.Null(message.Blocks);
    }

    [Fact]
    public void CreateMessage_SummaryWithSpecialCharacters_WrapsHtmlAndEscapesSummary()
    {
        var part = new LlmResponsePart("ответ", "<p><b>ответ</b></p>", "Открыть <ответ> & код");

        var message = part.CreateMessage();

        Assert.Equal("ответ", part.Text);
        Assert.Equal("<details><summary>Открыть &lt;ответ&gt; &amp; код</summary><p><b>ответ</b></p></details>", message.Html);
        Assert.Null(message.Blocks);
    }

    [Fact]
    public void CreateMessage_PlainTextWithoutSummary_ReturnsLiteralTypedParagraph()
    {
        const string text = "**ответ** <tag> &\nhttps://example.com";
        var part = new LlmResponsePart(text, "<p>formatted</p>", null);

        var message = part.CreateMessage(plainText: true);

        Assert.Null(message.Html);
        var paragraph = Assert.IsType<InputRichBlockParagraph>(Assert.Single(message.Blocks!));
        Assert.Equal(text, Assert.IsType<RichTextText>(paragraph.Text).Text);
    }

    [Fact]
    public void CreateMessage_PlainTextWithSummary_PreservesSummaryAndHistoryText()
    {
        const string text = "ссылка (https://example.com)";
        const string summary = "Открыть <ответ> & код";
        var part = new LlmResponsePart(text, "<p><a href=\"https://example.com\">ссылка</a></p>", summary);
        var original = part.CreateMessage();

        var message = part.CreateMessage(plainText: true);

        Assert.Null(message.Html);
        var details = Assert.IsType<InputRichBlockDetails>(Assert.Single(message.Blocks!));
        Assert.Equal(summary, Assert.IsType<RichTextText>(details.Summary).Text);
        var paragraph = Assert.IsType<InputRichBlockParagraph>(Assert.Single(details.Blocks));
        Assert.Equal(text, Assert.IsType<RichTextText>(paragraph.Text).Text);
        Assert.Equal(text, part.Text);
        Assert.Equal(original.Html, part.CreateMessage().Html);
        Assert.Null(part.CreateMessage().Blocks);
    }

    [Fact]
    public void Constructor_NullTextOrHtml_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("text", () => new LlmResponsePart(null!, "<p>ответ</p>", null));
        Assert.Throws<ArgumentNullException>("html", () => new LlmResponsePart("ответ", null!, null));
    }
}

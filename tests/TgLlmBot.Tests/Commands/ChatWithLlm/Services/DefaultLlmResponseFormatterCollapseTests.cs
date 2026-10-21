using System;
using System.Linq;
using Telegram.Bot.Types;
using TgLlmBot.Commands.ChatWithLlm.Services;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmResponseFormatterCollapseTests
{
    [Theory]
    [InlineData(599, false)]
    [InlineData(600, false)]
    [InlineData(601, true)]
    public void Format_TextNearCharacterThreshold_CollapsesOnlyAboveThreshold(int length, bool expectedCollapsed)
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true);
        var text = new string('a', length);

        var part = Assert.Single(formatter.Format(text));

        Assert.Equal(text, part.Text);
        Assert.Equal(expectedCollapsed, FormatterTestSupport.ParseHtml(part).Elements("details").Any());
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void Format_TextNearLineThreshold_CollapsesOnlyAboveThreshold(int lines, bool expectedCollapsed)
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: int.MaxValue);
        var markdown = string.Join('\n', Enumerable.Repeat("строка", lines));

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal(markdown, part.Text);
        Assert.Equal(expectedCollapsed, FormatterTestSupport.ParseHtml(part).Elements("details").Any());
    }

    [Theory]
    [InlineData("**ответ**", 5, false)]
    [InlineData("**ответ**", 4, true)]
    [InlineData("&amp;", 1, false)]
    [InlineData("😀", 1, false)]
    [InlineData("😀😀", 1, true)]
    [InlineData("e\u0301", 1, true)]
    [InlineData("e\u0301", 2, false)]
    public void Format_MarkdownAndUnicode_CountsRenderedCodePointsForCollapse(string markdown, int threshold, bool expectedCollapsed)
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: threshold);

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal(expectedCollapsed, FormatterTestSupport.ParseHtml(part).Elements("details").Any());
    }

    [Fact]
    public void Format_LinkWithHiddenUrl_CurrentlyIncludesUrlInCharacterThreshold()
    {
        // Characterize the current history-text policy: HTML displays "x", but Text also contains the URL.
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: 10);

        var part = Assert.Single(formatter.Format("[x](https://example.com)"));

        Assert.Equal("x (https://example.com)", part.Text);
        Assert.Equal("<details><summary>Развернуть ответ</summary><p><a href=\"https://example.com\">x</a></p></details>", part.CreateMessage().Html);
    }

    [Fact]
    public void Format_ParagraphSeparators_IncludesSeparatorsInLineCount()
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, lineThreshold: 2);

        var part = Assert.Single(formatter.Format("# Заголовок\n\nОтвет"));

        Assert.Equal("Заголовок\n\nОтвет", part.Text);
        Assert.Equal("<details><summary>Развернуть ответ</summary><h1>Заголовок</h1><p>Ответ</p></details>", part.CreateMessage().Html);
    }

    [Fact]
    public void Format_CollapseDisabled_KeepsAllPartsExpanded()
    {
        var formatter = FormatterTestSupport.CreateFormatter(characterThreshold: 1, lineThreshold: 1);
        var markdown = new string('a', 5000) + "\nb";

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, markdown);
        Assert.All(parts, static part => Assert.Empty(FormatterTestSupport.ParseHtml(part).Descendants("details")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Format_WholeResponseExceedingThreshold_CollapsesAndNumbersEveryPart(bool plainText)
    {
        // Neither part alone exceeds 4500 characters. The whole response does.
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: 4500);
        var text = new string('a', 5000);

        var parts = plainText ? formatter.FormatPlainText(text) : formatter.Format(text);

        FormatterTestSupport.AssertPreservesText(parts, text);
        Assert.Collection(parts,
            static part => Assert.Equal("<details><summary>Развернуть ответ &#183; 1/2</summary><p>" + new string('a', 4096) + "</p></details>", part.CreateMessage().Html),
            static part => Assert.Equal("<details><summary>Развернуть ответ &#183; 2/2</summary><p>" + new string('a', 904) + "</p></details>", part.CreateMessage().Html));
    }

    [Fact]
    public void FormatPlainText_NewlineAcrossPartBoundary_CountsOnlyExplicitLines()
    {
        var text = new string('a', 4096) + "\nb";
        int[] expectedLengths = [4096, 2];
        string[] expectedSummaries = ["Развернуть ответ · 1/2", "Развернуть ответ · 2/2"];
        var expandedFormatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: int.MaxValue, lineThreshold: 2);
        var collapsedFormatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: int.MaxValue, lineThreshold: 1);

        var expanded = expandedFormatter.FormatPlainText(text);
        var collapsed = collapsedFormatter.FormatPlainText(text);

        Assert.Equal(expectedLengths, expanded.Select(static part => part.Text.Length));
        Assert.All(expanded, static part => Assert.Empty(FormatterTestSupport.ParseHtml(part).Descendants("details")));
        Assert.Equal(expectedSummaries,
            collapsed.Select(static part => Assert.Single(FormatterTestSupport.ParseHtml(part).Descendants("summary")).Value));
    }

    [Fact]
    public void Format_MultipartResponseBelowThresholds_KeepsAllPartsExpanded()
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: 6000);
        var text = new string('a', 5000);
        int[] expectedLengths = [4096, 904];

        var parts = formatter.Format(text);

        Assert.Equal(expectedLengths, parts.Select(static part => part.Text.Length));
        Assert.All(parts, static part => Assert.Empty(FormatterTestSupport.ParseHtml(part).Descendants("details")));
    }

    [Fact]
    public void FormatPlainText_MarkdownExceedingThreshold_CollapsesAndPreservesLiteralMarkup()
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true, characterThreshold: 5);

        var part = Assert.Single(formatter.FormatPlainText("**ответ**"));
        var message = part.CreateMessage(plainText: true);

        Assert.Equal("**ответ**", part.Text);
        var details = Assert.IsType<InputRichBlockDetails>(Assert.Single(message.Blocks!));
        Assert.Equal("Развернуть ответ", Assert.IsType<RichTextText>(details.Summary).Text);
        var paragraph = Assert.IsType<InputRichBlockParagraph>(Assert.Single(details.Blocks));
        Assert.Equal("**ответ**", Assert.IsType<RichTextText>(paragraph.Text).Text);
    }

    [Theory]
    [InlineData(0, 8, "characterThreshold")]
    [InlineData(-1, 8, "characterThreshold")]
    [InlineData(600, 0, "lineThreshold")]
    [InlineData(600, -1, "lineThreshold")]
    public void Constructor_NonPositiveThresholds_ThrowsArgumentOutOfRangeException(int characters, int lines, string parameterName)
    {
        Assert.Throws<ArgumentOutOfRangeException>(parameterName, () => new DefaultLlmResponseFormatterOptions(true, characters, lines));
    }
}

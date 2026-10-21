using System;
using System.Linq;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmResponseFormatterSplittingTests
{
    [Theory]
    [InlineData(4095, new[] { 4095 })]
    [InlineData(4096, new[] { 4096 })]
    [InlineData(4097, new[] { 4096, 1 })]
    [InlineData(8192, new[] { 4096, 4096 })]
    [InlineData(8193, new[] { 4096, 4096, 1 })]
    public void Format_TextNearHistoryLengthLimit_RespectsLimitInBothModes(int length, int[] expectedPartLengths)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = new string('a', length);

        var formatted = formatter.Format(text);
        var plain = formatter.FormatPlainText(text);

        Assert.Equal(expectedPartLengths, formatted.Select(static part => part.Text.Length));
        Assert.Equal(expectedPartLengths, plain.Select(static part => part.Text.Length));
        FormatterTestSupport.AssertPreservesText(formatted, text);
        FormatterTestSupport.AssertPreservesText(plain, text);
        Assert.All(formatted, static part => Assert.Equal($"<p>{part.Text}</p>", part.CreateMessage().Html));
        Assert.All(plain, static part => Assert.Equal($"<p>{part.Text}</p>", part.CreateMessage().Html));
    }

    [Theory]
    [InlineData("**", "<p><b>", "</b></p>")]
    [InlineData("*", "<p><i>", "</i></p>")]
    [InlineData("~~", "<p><s>", "</s></p>")]
    [InlineData("||", "<p><tg-spoiler>", "</tg-spoiler></p>")]
    [InlineData("`", "<p><code>", "</code></p>")]
    public void Format_InlineFormattingAcrossParts_ClosesAndReopensTags(string delimiter, string openingTags, string closingTags)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = new string('a', 5000);

        var parts = formatter.Format(delimiter + text + delimiter);

        FormatterTestSupport.AssertPreservesText(parts, text);
        Assert.Collection(parts,
            static part => Assert.Equal(new string('a', 4096), part.Text),
            static part => Assert.Equal(new string('a', 904), part.Text));
        Assert.All(parts, part => Assert.Equal(openingTags + part.Text + closingTags, part.CreateMessage().Html));
    }

    [Fact]
    public void Format_FencedCodeAcrossParts_PreservesCodeTagsAndLanguage()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = new string('a', 5000);

        var parts = formatter.Format($"```csharp\n{text}\n```");

        FormatterTestSupport.AssertPreservesText(parts, text);
        Assert.All(parts, static part => Assert.Equal($"<pre><code class=\"language-csharp\">{part.Text}</code></pre>", part.CreateMessage().Html));
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\n')]
    [InlineData('\t')]
    public void Format_LongWhitespaceBetweenContent_PreservesEveryCharacterInCodeAndPlainText(char whitespace)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = "a\n" + new string(whitespace, 8192) + "\nb";

        var code = formatter.Format($"```csharp\n{text}\n```");
        var plain = formatter.FormatPlainText(text);

        FormatterTestSupport.AssertPreservesText(code, text);
        FormatterTestSupport.AssertPreservesText(plain, text);
        Assert.All(code, static part => Assert.Equal(part.Text, Assert.Single(FormatterTestSupport.ParseHtml(part).Descendants("code")).Value));
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\n')]
    [InlineData('\t')]
    public void FormatPlainText_NearbyWordOrLineBoundary_SplitsAtBoundaryAndPreservesSeparator(char separator)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var first = new string('a', 3000) + separator;
        var second = new string('b', 2000);

        var parts = formatter.FormatPlainText(first + second);

        Assert.Collection(parts,
            part => Assert.Equal(first, part.Text),
            part => Assert.Equal(second, part.Text));
        FormatterTestSupport.AssertPreservesText(parts, first + second);
        Assert.All(parts, static part => FormatterTestSupport.ParseHtml(part));
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("e\u0301")]
    [InlineData("👩‍💻")]
    [InlineData("🇷🇺")]
    public void Format_GraphemeAtPartBoundary_KeepsGraphemeTogetherInBothModes(string grapheme)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var first = new string('a', 4095);
        var second = grapheme + "b";
        var text = first + second;

        var formatted = formatter.Format(text);
        var plain = formatter.FormatPlainText(text);

        Assert.Collection(formatted,
            part => Assert.Equal(first, part.Text),
            part => Assert.Equal(second, part.Text));
        Assert.Collection(plain,
            part => Assert.Equal(first, part.Text),
            part => Assert.Equal(second, part.Text));
        FormatterTestSupport.AssertPreservesText(formatted, text);
        FormatterTestSupport.AssertPreservesText(plain, text);
    }

    [Theory]
    [InlineData("e&#x301;b", "e\u0301b")]
    [InlineData("**e**\u0301b", "e\u0301b")]
    [InlineData("&#x1F469;&#x200D;&#x1F4BB;b", "👩‍💻b")]
    public void Format_GraphemeAcrossMarkdownNodes_KeepsGraphemeInSamePart(string markdownSuffix, string expectedSuffix)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var prefix = new string('a', 4095);

        var parts = formatter.Format(prefix + markdownSuffix);

        FormatterTestSupport.AssertPreservesText(parts, prefix + expectedSuffix);
        Assert.Collection(parts,
            part => Assert.Equal(prefix, part.Text),
            part => Assert.Equal(expectedSuffix, part.Text));
        Assert.All(parts, static part => FormatterTestSupport.ParseHtml(part));
    }

    [Fact]
    public void FormatPlainText_CombiningMarkAfterSpace_DoesNotSplitGraphemeAtWordBoundary()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = new string('a', 3000) + " \u0301" + new string('b', 2000);

        var parts = formatter.FormatPlainText(text);

        FormatterTestSupport.AssertPreservesText(parts, text);
        Assert.DoesNotContain(parts, static part => part.Text.StartsWith('\u0301'));
    }

    [Fact]
    public void FormatPlainText_GraphemeExceedingPartLimit_SplitsWithoutLosingContentOrSurrogatePairs()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        // One grapheme exceeds the storage limit, so it must be split even though normal graphemes stay intact.
        var text = "a" + new string('\u0301', 8192) + "😀";

        var parts = formatter.FormatPlainText(text);

        FormatterTestSupport.AssertPreservesText(parts, text);
        Assert.All(parts, static part =>
        {
            Assert.False(char.IsLowSurrogate(part.Text[0]));
            Assert.False(char.IsHighSurrogate(part.Text[^1]));
        });
    }

    [Fact]
    public void Format_LinkExceedingRemainingCapacity_MovesLabelAndUrlToSamePart()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var prefix = new string('a', 4080) + " ";

        var parts = formatter.Format(prefix + "[link](https://example.com)");

        Assert.Collection(parts,
            part =>
            {
                Assert.Equal(prefix, part.Text);
                Assert.Equal($"<p>{prefix}</p>", part.CreateMessage().Html);
            },
            static part =>
            {
                Assert.Equal("link (https://example.com)", part.Text);
                Assert.Equal("<p><a href=\"https://example.com\">link</a></p>", part.CreateMessage().Html);
            });
    }

    [Fact]
    public void Format_LinkExceedingPartLimit_RendersVisiblePlainTextAcrossParts()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var url = "https://example.com/" + new string('a', 5000);
        var expectedText = $"link ({url})";

        var parts = formatter.Format($"[link]({url})");

        FormatterTestSupport.AssertPreservesText(parts, expectedText);
        Assert.All(parts, static part =>
        {
            var html = FormatterTestSupport.ParseHtml(part);
            Assert.Empty(html.Descendants("a"));
            Assert.Equal(part.Text, html.Value);
        });
    }

    [Fact]
    public void Format_ListAndQuoteAcrossParts_ClosesAndReopensBlockTags()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var text = new string('a', 5000);

        var parts = formatter.Format($"> - {text}");

        FormatterTestSupport.AssertPreservesText(parts, "• " + text);
        Assert.All(parts, static part =>
        {
            var html = FormatterTestSupport.ParseHtml(part);
            var quote = Assert.Single(html.Elements("blockquote"));
            var list = Assert.Single(quote.Elements("ul"));
            var item = Assert.Single(list.Elements("li"));
            var paragraph = Assert.Single(item.Elements("p"));
            Assert.NotEmpty(paragraph.Value);
        });
    }
}

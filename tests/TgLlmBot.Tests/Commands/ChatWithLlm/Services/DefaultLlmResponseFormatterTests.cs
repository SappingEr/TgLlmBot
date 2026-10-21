using System;
using TgLlmBot.Commands.ChatWithLlm.Services;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmResponseFormatterTests
{
    [Theory]
    [InlineData("Привет", "Привет", "<p>Привет</p>")]
    [InlineData("Первый\n\nВторой", "Первый\n\nВторой", "<p>Первый</p><p>Второй</p>")]
    [InlineData("Первая\nвторая", "Первая\nвторая", "<p>Первая<br/>вторая</p>")]
    [InlineData("Первая  \nвторая", "Первая\nвторая", "<p>Первая<br/>вторая</p>")]
    [InlineData("# Заголовок", "Заголовок", "<h1>Заголовок</h1>")]
    [InlineData("###### Заголовок", "Заголовок", "<h6>Заголовок</h6>")]
    [InlineData("**жирный** и *курсив*", "жирный и курсив", "<p><b>жирный</b> и <i>курсив</i></p>")]
    [InlineData("**жирный *курсив***", "жирный курсив", "<p><b>жирный <i>курсив</i></b></p>")]
    [InlineData("~~удалён~~", "удалён", "<p><s>удалён</s></p>")]
    [InlineData("||секрет||", "секрет", "<p><tg-spoiler>секрет</tg-spoiler></p>")]
    [InlineData("`a < b && c`", "a < b && c", "<p><code>a &lt; b &amp;&amp; c</code></p>")]
    [InlineData("```csharp\na < b\nnext();\n```", "a < b\nnext();", "<pre><code class=\"language-csharp\">a &lt; b\nnext();</code></pre>")]
    [InlineData("```\nкод\n```", "код", "<pre><code>код</code></pre>")]
    [InlineData("    код", "код", "<pre><code>код</code></pre>")]
    [InlineData("> Цитата", "Цитата", "<blockquote><p>Цитата</p></blockquote>")]
    [InlineData("- один\n- два", "• один\n• два", "<ul><li><p>один</p></li><li><p>два</p></li></ul>")]
    [InlineData("3. три\n4. четыре", "3. три\n4. четыре", "<ol><li value=\"3\"><p>три</p></li><li value=\"4\"><p>четыре</p></li></ol>")]
    [InlineData("---", "───", "<p>───</p>")]
    [InlineData("&lt;текст&gt; &amp;", "<текст> &", "<p>&lt;текст&gt; &amp;</p>")]
    [InlineData("До <b>текст</b>", "До <b>текст</b>", "<p>До &lt;b&gt;текст&lt;/b&gt;</p>")]
    [InlineData("<div>текст</div>", "<div>текст</div>", "<p>&lt;div&gt;текст&lt;/div&gt;</p>")]
    [InlineData("[сайт](https://example.com)", "сайт (https://example.com)", "<p><a href=\"https://example.com\">сайт</a></p>")]
    [InlineData("[**сайт**](https://example.com)", "сайт (https://example.com)", "<p><a href=\"https://example.com\"><b>сайт</b></a></p>")]
    [InlineData("[сайт](https://example.com/?a=1&b=2)", "сайт (https://example.com/?a=1&b=2)", "<p><a href=\"https://example.com/?a=1&amp;b=2\">сайт</a></p>")]
    [InlineData("[https://example.com](https://example.com)", "https://example.com", "<p><a href=\"https://example.com\">https://example.com</a></p>")]
    [InlineData("<https://example.com>", "https://example.com", "<p><a href=\"https://example.com\">https://example.com</a></p>")]
    [InlineData("<user@example.com>", "user@example.com", "<p><a href=\"mailto:user@example.com\">user@example.com</a></p>")]
    [InlineData("![описание](https://example.com/image.png)", "описание (https://example.com/image.png)", "<p><a href=\"https://example.com/image.png\">описание</a></p>")]
    [InlineData("| A | B |\n| --- | --- |\n| 1 | 2 |", "A | B\n1 | 2", "<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>")]
    public void Format_SupportedMarkdown_ProducesExpectedHistoryTextAndHtml(string markdown, string expectedText, string expectedHtml)
    {
        var formatter = FormatterTestSupport.CreateFormatter();

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal(expectedText, part.Text);
        Assert.Equal(expectedHtml, part.CreateMessage().Html);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n\n")]
    public void Format_EmptyOrWhitespaceInput_ReturnsNoParts(string input)
    {
        var formatter = FormatterTestSupport.CreateFormatter();

        Assert.Empty(formatter.Format(input));
        Assert.Empty(formatter.FormatPlainText(input));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void Format_VariousLineEndings_NormalizesToLfInBothModes(string lineEnding)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var input = $"первая{lineEnding}вторая";

        var formatted = Assert.Single(formatter.Format(input));
        var plain = Assert.Single(formatter.FormatPlainText(input));

        Assert.Equal("первая\nвторая", formatted.Text);
        Assert.Equal("<p>первая<br/>вторая</p>", formatted.CreateMessage().Html);
        Assert.Equal(formatted.Text, plain.Text);
        Assert.Equal(formatted.CreateMessage().Html, plain.CreateMessage().Html);
    }

    [Theory]
    [InlineData('\uD800')]
    [InlineData('\uDC00')]
    public void Format_UnpairedSurrogates_ReplacesWithReplacementCharactersInBothModes(char invalidSurrogate)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var input = $"до{invalidSurrogate}после";

        var formatted = Assert.Single(formatter.Format(input));
        var plain = Assert.Single(formatter.FormatPlainText(input));

        Assert.Equal("до\uFFFDпосле", formatted.Text);
        Assert.Equal("<p>до\uFFFDпосле</p>", formatted.CreateMessage().Html);
        Assert.Equal(formatted.Text, plain.Text);
        Assert.Equal(formatted.CreateMessage().Html, plain.CreateMessage().Html);
    }

    [Fact]
    public void FormatPlainText_MarkdownAndHtml_PreservesMarkdownAndEscapesHtml()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        const string text = "**текст**\n<script>x & y</script>";

        var part = Assert.Single(formatter.FormatPlainText(text));

        Assert.Equal(text, part.Text);
        Assert.Equal("<p>**текст**<br/>&lt;script&gt;x &amp; y&lt;/script&gt;</p>", part.CreateMessage().Html);
    }

    [Fact]
    public void Format_TaskCheckboxes_PreservesStatesAndItemText()
    {
        var formatter = FormatterTestSupport.CreateFormatter();

        var part = Assert.Single(formatter.Format("- [x] готово\n- [ ] позже"));

        Assert.Equal("• ☑ готово • ☐ позже", string.Join(' ', part.Text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries)));
        Assert.Collection(FormatterTestSupport.ParseHtml(part).Descendants("li"),
            static item => Assert.Equal("☑ готово", string.Join(' ', item.Value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))),
            static item => Assert.Equal("☐ позже", string.Join(' ', item.Value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))));
    }

    [Fact]
    public void Format_ShortResponseAfterLongResponse_ReturnsExpandedResponse()
    {
        var formatter = FormatterTestSupport.CreateFormatter(collapse: true);
        formatter.Format(new string('a', 5000));

        var part = Assert.Single(formatter.Format("короткий"));

        Assert.Equal("короткий", part.Text);
        Assert.Equal("<p>короткий</p>", part.CreateMessage().Html);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("options", () => new DefaultLlmResponseFormatter(null!));
    }

    [Fact]
    public void Format_NullMarkdown_ThrowsArgumentNullException()
    {
        var formatter = FormatterTestSupport.CreateFormatter();

        Assert.Throws<ArgumentNullException>("markdown", () => formatter.Format(null!));
    }

    [Fact]
    public void FormatPlainText_NullText_ThrowsArgumentNullException()
    {
        var formatter = FormatterTestSupport.CreateFormatter();

        Assert.Throws<ArgumentNullException>("text", () => formatter.FormatPlainText(null!));
    }
}

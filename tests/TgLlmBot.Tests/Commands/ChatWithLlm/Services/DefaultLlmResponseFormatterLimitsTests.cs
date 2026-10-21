using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

public sealed class DefaultLlmResponseFormatterLimitsTests
{
    [Theory]
    [InlineData(false, 99)]
    [InlineData(false, 100)]
    [InlineData(false, 101)]
    [InlineData(true, 99)]
    [InlineData(true, 100)]
    [InlineData(true, 101)]
    public void Format_ListNearBlockLimit_PreservesItemsWithoutEmptyDuplicates(bool ordered, int count)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var labels = Enumerable.Range(1, count).Select(static number => $"item{number}").ToArray();
        var markdown = string.Join('\n', labels.Select((label, index) => $"{(ordered ? $"{index + 1}." : "-")} {label}"));
        var expectedText = string.Join('\n', labels.Select((label, index) => $"{(ordered ? $"{index + 1}." : "•")} {label}"));

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, expectedText);
        var roots = parts.Select(FormatterTestSupport.ParseHtml).ToArray();
        Assert.Equal(labels, roots.SelectMany(static root => root.Descendants("li")).Select(static item => item.Value));
        Assert.All(roots, static root => Assert.InRange(root.Descendants().Count(), 1, 200));
        if (ordered)
        {
            Assert.Equal(Enumerable.Range(1, count), roots.SelectMany(static root => root.Descendants("li")).Select(static item => (int) item.Attribute("value")!));
        }
    }

    [Theory]
    [InlineData(200, new[] { 200 })]
    [InlineData(201, new[] { 200, 1 })]
    public void Format_ParagraphCountNearBlockLimit_SplitsBeforeExceedingLimit(int paragraphs, int[] expectedParagraphCounts)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var markdown = string.Join("\n\n", Enumerable.Repeat("x", paragraphs));

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, markdown);
        Assert.Equal(expectedParagraphCounts, parts.Select(static part => FormatterTestSupport.ParseHtml(part).Elements("p").Count()));
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(13, true)]
    public void Format_QuoteDepthNearFormattingLimit_FlattensOnlyAboveLimit(int quoteDepth, bool expectedPlainText)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var markdown = string.Concat(Enumerable.Repeat("> ", quoteDepth)) + "ответ";

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal("ответ", part.Text);
        var html = FormatterTestSupport.ParseHtml(part);
        if (expectedPlainText)
        {
            Assert.Equal("<p>ответ</p>", part.CreateMessage().Html);
        }
        else
        {
            Assert.Equal(quoteDepth, html.Descendants("blockquote").Count());
            Assert.Equal("ответ", Assert.Single(html.Descendants("p")).Value);
        }
    }

    [Fact]
    public void Format_DeepFormattingAcrossParts_PreservesSeparatorsListMarkersAndUrls()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var quote = string.Concat(Enumerable.Repeat("> ", 13));
        var text = new string('a', 5000);
        var markdown = $"{quote}- {text}\n{quote}- [ссылка](https://example.com/?a=1&b=2)";
        var expectedText = "• " + text + "\n• ссылка (https://example.com/?a=1&b=2)";

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, expectedText);
        Assert.All(parts, static part =>
        {
            var html = FormatterTestSupport.ParseHtml(part);
            var paragraph = Assert.Single(html.Elements());
            Assert.Equal("p", paragraph.Name.LocalName);
            Assert.All(paragraph.Elements(), static element => Assert.Equal("br", element.Name.LocalName));
            // XElement.Value omits <br/>. Reconstruct newlines independently of the formatter.
            var reconstructedText = string.Concat(paragraph.Nodes().Select(static node => node is XElement ? "\n" : ((XText) node).Value));
            Assert.Equal(part.Text, reconstructedText);
        });
    }

    [Theory]
    [InlineData(20, "table")]
    [InlineData(21, "pre")]
    public void Format_TableColumnCountNearLimit_UsesPreformattedTextOnlyAboveLimit(int columns, string expectedTag)
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var header = string.Join(" | ", Enumerable.Repeat("H", columns));
        var delimiter = string.Join(" | ", Enumerable.Repeat("---", columns));
        var values = string.Join(" | ", Enumerable.Repeat("v", columns));
        var markdown = $"| {header} |\n| {delimiter} |\n| {values} |";

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal(header + "\n" + values, part.Text);
        var block = Assert.Single(FormatterTestSupport.ParseHtml(part).Elements());
        Assert.Equal(expectedTag, block.Name.LocalName);
        if (expectedTag == "pre")
        {
            Assert.Equal(part.Text, block.Value);
            Assert.Empty(block.Elements());
        }
        else
        {
            Assert.Equal(columns, block.Descendants("th").Count());
            Assert.Equal(columns, block.Descendants("td").Count());
        }
    }

    [Fact]
    public void Format_WideTableWithFormattingAndLinks_FlattensFormattingAndDisplaysUrls()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var header = string.Join(" | ", Enumerable.Repeat("H", 21));
        var delimiter = string.Join(" | ", Enumerable.Repeat("---", 21));
        var remainingCells = string.Join(" | ", Enumerable.Repeat("v", 19));
        var markdown = $"| {header} |\n| {delimiter} |\n| **bold** | [link](https://example.com) | {remainingCells} |";
        var expectedText = $"{header}\nbold | link (https://example.com) | {remainingCells}";

        var part = Assert.Single(formatter.Format(markdown));

        Assert.Equal(expectedText, part.Text);
        Assert.Equal($"<pre>{expectedText}</pre>", part.CreateMessage().Html);
    }

    [Fact]
    public void Format_TableRowExceedingRemainingCapacity_MovesWholeRowToNextPart()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var cell = new string('a', 4090);
        var markdown = $"| A | B |\n| --- | --- |\n| {cell} | b |";

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, $"A | B\n{cell} | b");
        Assert.Collection(parts,
            static part =>
            {
                Assert.Equal("A | B\n", part.Text);
                Assert.Equal("<table><tr><th>A</th><th>B</th></tr></table>", part.CreateMessage().Html);
            },
            part =>
            {
                Assert.Equal($"{cell} | b", part.Text);
                Assert.Equal($"<table><tr><td>{cell}</td><td>b</td></tr></table>", part.CreateMessage().Html);
            });
    }

    [Fact]
    public void Format_TableRowsExceedingBlockLimit_MovesWholeRowsToNextPart()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var rows = string.Concat(Enumerable.Repeat("\n| a | b |", 100));
        var expectedText = "A | B" + string.Concat(Enumerable.Repeat("\na | b", 100));
        int[] expectedRowCounts = [66, 35];

        var parts = formatter.Format("| A | B |\n| --- | --- |" + rows);

        FormatterTestSupport.AssertPreservesText(parts, expectedText);
        Assert.Equal(expectedRowCounts, parts.Select(static part => FormatterTestSupport.ParseHtml(part).Descendants("tr").Count()));
        Assert.All(parts, static part =>
        {
            var table = Assert.Single(FormatterTestSupport.ParseHtml(part).Elements("table"));
            Assert.InRange(table.DescendantsAndSelf().Count(), 1, 200);
            Assert.All(table.Elements("tr"), static row =>
                Assert.Collection(row.Elements(),
                    static cell => Assert.Equal(cell.Name.LocalName == "th" ? "A" : "a", cell.Value),
                    static cell => Assert.Equal(cell.Name.LocalName == "th" ? "B" : "b", cell.Value)));
        });
    }

    [Fact]
    public void Format_TableCellAcrossParts_PreservesColumnPositions()
    {
        var formatter = FormatterTestSupport.CreateFormatter();
        var cell = new string('a', 5000);
        var markdown = $"| A | B | C |\n| --- | --- | --- |\n| left | {cell} | right |";

        var parts = formatter.Format(markdown);

        FormatterTestSupport.AssertPreservesText(parts, $"A | B | C\nleft | {cell} | right");
        var rows = parts.SelectMany(static part => FormatterTestSupport.ParseHtml(part).Descendants("tr"))
            .Where(static row => row.Elements("td").Any()).ToArray();
        Assert.Collection(rows,
            static row => Assert.Collection(row.Elements("td"),
                static first => Assert.Equal("left", first.Value),
                static second => Assert.NotEmpty(second.Value),
                static third => Assert.Empty(third.Value)),
            static row => Assert.Collection(row.Elements("td"),
                static first => Assert.Empty(first.Value),
                static second => Assert.NotEmpty(second.Value),
                static third => Assert.Equal("right", third.Value)));
        Assert.Equal(cell, string.Concat(rows.Select(static row => row.Elements("td").ElementAt(1).Value)));
    }
}

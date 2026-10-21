using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using TgLlmBot.Commands.ChatWithLlm.Services;
using Xunit;

namespace TgLlmBot.Tests.Commands.ChatWithLlm.Services;

internal static class FormatterTestSupport
{
    public static DefaultLlmResponseFormatter CreateFormatter(bool collapse = false, int characterThreshold = 600, int lineThreshold = 8)
    {
        return new(new(collapse, characterThreshold, lineThreshold));
    }

    public static void AssertPreservesText(IReadOnlyCollection<LlmResponsePart> parts, string expectedText)
    {
        Assert.Equal(expectedText, string.Concat(parts.Select(static part => part.Text)));
        Assert.All(parts, static part => Assert.InRange(part.Text.Length, 1, 4096));
    }

    public static XElement ParseHtml(LlmResponsePart part)
    {
        var html = part.CreateMessage().Html;
        Assert.NotNull(html);
        // Wrapping permits multiple top-level blocks. Parsing also checks that each part's tags are balanced.
        return XElement.Parse($"<root>{html}</root>");
    }
}

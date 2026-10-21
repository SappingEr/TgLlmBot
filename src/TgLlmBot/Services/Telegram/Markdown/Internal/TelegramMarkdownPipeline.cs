using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;

namespace TgLlmBot.Services.Telegram.Markdown.Internal;

internal static class TelegramMarkdownPipeline
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseSpoilers()
        .UseAlertBlocks()
        .UseAutoIdentifiers()
        .UseCustomContainers()
        .UseDefinitionLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseGridTables()
        .UseMediaLinks()
        .UsePipeTables()
        .UseListExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .UseGenericAttributes() // Must be last as it is one parser that is modifying other parsers
        .Build();

    public static MarkdownDocument Parse(string normalMarkdown)
    {
        return Markdig.Markdown.Parse(normalMarkdown, Pipeline);
    }
}

using TgLlmBot.Services.Telegram.Markdown.Internal;

namespace TgLlmBot.Services.Telegram.Markdown;

/// <summary>
///     Converts Markdown to Telegram MarkdownV2 format with proper escaping.
/// </summary>
public class DefaultTelegramMarkdownConverter : ITelegramMarkdownConverter
{
    public string[] ConvertToPartedTelegramMarkdown(string normalMarkdown, int partLengthLimit)
    {
        var document = TelegramMarkdownPipeline.Parse(normalMarkdown);
        var parts = new TelegramMarkdownRenderer().RenderParted(document, partLengthLimit);
        return parts;
    }

    public string ConvertToSolidTelegramMarkdown(string normalMarkdown)
    {
        var document = TelegramMarkdownPipeline.Parse(normalMarkdown);
        var result = new TelegramMarkdownRenderer().Render(document);
        return result;
    }
}

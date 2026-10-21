namespace TgLlmBot.Services.Telegram.Markdown.Internal;

internal sealed class TelegramRichTextPart(string text, string html)
{
    public string Text { get; } = text;

    public string Html { get; } = html;
}

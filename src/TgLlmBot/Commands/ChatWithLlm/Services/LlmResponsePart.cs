using System;
using System.Net;
using Telegram.Bot.Types;

namespace TgLlmBot.Commands.ChatWithLlm.Services;

public class LlmResponsePart
{
    private readonly string _html;
    private readonly string? _summary;

    public LlmResponsePart(string text, string html, string? summary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(html);
        Text = text;
        _html = html;
        _summary = summary;
    }

    /// <summary>
    ///     Полный текст именно этой части для истории и повторной отправки, включая URL ссылок,
    ///     без заголовка и оболочки ката.
    /// </summary>
    public string Text { get; }

    public InputRichMessage CreateMessage(bool plainText = false)
    {
        if (!plainText)
        {
            return new()
            {
                Html = _summary is null ? _html : $"<details><summary>{WebUtility.HtmlEncode(_summary)}</summary>{_html}</details>"
            };
        }

        InputRichBlock[] blocks = [new InputRichBlockParagraph { Text = new RichTextText { Text = Text } }];
        if (_summary is not null)
        {
            blocks = [new InputRichBlockDetails { Summary = new RichTextText { Text = _summary }, Blocks = blocks }];
        }

        // При ошибке HTML отправляем типизированные блоки: текст больше не проходит парсер,
        // а длинный ответ остаётся свёрнутым и сохраняет то же содержимое.
        return new() { Blocks = blocks };
    }
}

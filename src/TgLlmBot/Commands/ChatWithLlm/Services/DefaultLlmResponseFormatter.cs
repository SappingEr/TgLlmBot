using System;
using System.Globalization;
using TgLlmBot.Services.Telegram.Markdown.Internal;
using TgLlmBot.Utils;

namespace TgLlmBot.Commands.ChatWithLlm.Services;

public class DefaultLlmResponseFormatter : ILlmResponseFormatter
{
    // У Rich Messages лимит больше, но одна отправленная часть по-прежнему соответствует
    // одной записи в истории с прежним ограничением в 4096 символов.
    private const int PartLengthLimit = 4096;

    private readonly DefaultLlmResponseFormatterOptions _options;

    public DefaultLlmResponseFormatter(DefaultLlmResponseFormatterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public LlmResponsePart[] Format(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var normalized = Normalize(markdown);
        var document = TelegramMarkdownPipeline.Parse(normalized);
        var parts = new TelegramRichTextRenderer(PartLengthLimit).RenderParted(document);
        return parts.Length == 0 ? FormatPlainText(normalized) : FormatParts(parts);
    }

    public LlmResponsePart[] FormatPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new TelegramRichTextRenderer(PartLengthLimit).RenderPlainText(Normalize(text));
        return FormatParts(parts);
    }

    private LlmResponsePart[] FormatParts(TelegramRichTextPart[] parts)
    {
        var characters = 0;
        var lines = 1;
        foreach (var part in parts)
        {
            foreach (var rune in part.Text.EnumerateRunes())
            {
                characters++;
                if (rune.Value == '\n')
                {
                    lines++;
                }
            }
        }

        var collapse = _options.CollapseLongResponses && (characters > _options.CharacterThreshold || lines > _options.LineThreshold);
        var result = new LlmResponsePart[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            string? summary = null;
            if (collapse)
            {
                summary = parts.Length == 1
                    ? "Развернуть ответ"
                    : string.Create(CultureInfo.InvariantCulture, $"Развернуть ответ · {i + 1}/{parts.Length}");
            }

            result[i] = new(parts[i].Text, parts[i].Html, summary);
        }

        return result;
    }

    private static string Normalize(string text)
    {
        return SurrogatePairSanitizer.SanitizeInvalidUtf16(text).ReplaceLineEndings("\n");
    }
}

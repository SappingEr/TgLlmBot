using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TgLlmBot.Services.Telegram.Markdown.Internal.Extensions.Spoiler;

namespace TgLlmBot.Services.Telegram.Markdown.Internal;

internal sealed class TelegramRichTextRenderer
{
    // Оставляем запас под внешний details: у Rich Messages есть ограничения и на число блоков,
    // и на вложенность. Размер текста ограничен прежним полем истории, а не лимитом Rich Messages.
    private const int BlockCountLimit = 200;
    private const int FormattingDepthLimit = 12;

    private readonly StringBuilder _html = new();
    private readonly bool _measureOnly;
    private readonly List<OpenTag> _openTags = [];
    private readonly int _partLengthLimit;
    private readonly List<TelegramRichTextPart> _parts = [];
    private readonly StringBuilder _text = new();
    private int _blockCount;
    private int[] _textBoundaries = [];
    private int _textOffset;
    private bool _hasText;
    private bool _preformatted;
    private bool _requiresPlainText;
    private int _trailingNewlines;

    public TelegramRichTextRenderer(int partLengthLimit, bool measureOnly = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partLengthLimit, 2);
        _partLengthLimit = partLengthLimit;
        _measureOnly = measureOnly;
    }

    public TelegramRichTextPart[] RenderParted(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_measureOnly)
        {
            var measured = new TelegramRichTextRenderer(int.MaxValue, measureOnly: true).RenderParted(document);
            PrepareTextBoundaries(measured.Length == 0 ? string.Empty : measured[0].Text);
        }

        foreach (var block in document)
        {
            RenderBlock(block);
        }

        FinishPart();
        return [.. _parts];
    }

    public TelegramRichTextPart[] RenderPlainText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        PrepareTextBoundaries(text);
        Open("<p>", "</p>");
        AppendText(text);
        Close();
        FinishPart();
        return [.. _parts];
    }

    private void RenderBlock(Block block, bool separate = true)
    {
        if (separate)
        {
            AppendSeparator(2);
        }

        switch (block)
        {
            case HeadingBlock heading:
                Open($"<h{heading.Level.ToString(CultureInfo.InvariantCulture)}>", $"</h{heading.Level.ToString(CultureInfo.InvariantCulture)}>");
                RenderInlines(heading.Inline);
                Close();
                break;
            case CodeBlock code:
                RenderCode(code);
                break;
            case ListBlock list:
                RenderList(list);
                break;
            case QuoteBlock quote:
                Open("<blockquote>", "</blockquote>");
                foreach (var child in quote)
                {
                    RenderBlock(child);
                }

                Close();
                break;
            case Table table:
                RenderTable(table);
                break;
            case HtmlBlock html:
                // HTML модели остаётся текстом: произвольные теги не становятся кнопками или медиа.
                Open("<p>", "</p>");
                AppendText(html.Lines.ToString());
                Close();
                break;
            case ThematicBreakBlock:
                Open("<p>", "</p>");
                AppendText("───");
                Close();
                break;
            case LeafBlock leaf:
                Open("<p>", "</p>");
                RenderInlines(leaf.Inline);
                Close();
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    RenderBlock(child);
                }

                break;
        }
    }

    private void RenderCode(CodeBlock code)
    {
        Open("<pre>", "</pre>");
        var language = code is FencedCodeBlock fenced ? fenced.Info?.Split(' ', 2)[0] : null;
        Open(string.IsNullOrEmpty(language) ? "<code>" : $"<code class=\"language-{WebUtility.HtmlEncode(language)}\">", "</code>", false);
        _preformatted = true;
        var firstLine = true;
        foreach (var line in code.Lines)
        {
            if (!firstLine)
            {
                AppendText("\n");
            }

            AppendText(line.ToString() ?? string.Empty);
            firstLine = false;
        }

        _preformatted = false;
        Close();
        Close();
    }

    private void RenderList(ListBlock list)
    {
        Open(list.IsOrdered ? "<ol>" : "<ul>", list.IsOrdered ? "</ol>" : "</ul>");
        foreach (var child in list)
        {
            var item = (ListItemBlock) child;
            AppendSeparator(1);
            if (!_measureOnly && _text.Length > 0)
            {
                var measured = new TelegramRichTextRenderer(int.MaxValue, measureOnly: true);
                measured.RenderListItem(item, list.IsOrdered);
                if (_blockCount + measured._blockCount > BlockCountLimit || measured._text.Length > _partLengthLimit - _text.Length)
                {
                    FinishPart();
                }
            }

            RenderListItem(item, list.IsOrdered);
        }

        Close();
    }

    private void RenderListItem(ListItemBlock item, bool ordered)
    {
        var number = item.Order.ToString(CultureInfo.InvariantCulture);
        Open(ordered ? $"<li value=\"{number}\">" : "<li>", "</li>");
        AppendText(ordered ? $"{number}. " : "• ", false);
        var firstBlock = true;
        foreach (var block in item)
        {
            RenderBlock(block, !firstBlock);
            firstBlock = false;
        }

        Close();
    }

    private void RenderTable(Table table)
    {
        var wide = table.ColumnDefinitions.Count > 20;
        Open(wide ? "<pre>" : "<table>", wide ? "</pre>" : "</table>");
        _preformatted = wide;
        var firstRow = true;
        foreach (var block in table)
        {
            if (block is not TableRow row)
            {
                continue;
            }

            if (!firstRow)
            {
                AppendText("\n", wide);
            }

            if (!wide && !_measureOnly)
            {
                // Строка, помещающаяся в одну часть, переносится целиком при любом из лимитов.
                // Для измерения используем тот же рендеринг, чтобы учитывать код, ссылки и entities.
                var measured = new TelegramRichTextRenderer(int.MaxValue, measureOnly: true);
                measured.RenderTableRow(row, false);
                if (_text.Length > 0 && (_blockCount + measured._blockCount > BlockCountLimit || measured._text.Length > _partLengthLimit - _text.Length))
                {
                    FinishPart();
                }
            }

            RenderTableRow(row, wide);
            firstRow = false;
        }

        _preformatted = false;
        Close();
    }

    private void RenderTableRow(TableRow row, bool wide)
    {
        if (!wide)
        {
            Open("<tr>", "</tr>");
        }

        var cellIndex = 0;
        foreach (var child in row)
        {
            if (child is not TableCell cell)
            {
                continue;
            }

            if (!wide)
            {
                Open(row.IsHeader ? "<th>" : "<td>", row.IsHeader ? "</th>" : "</td>", precedingCells: cellIndex, followingCells: row.Count - cellIndex - 1);
            }

            if (cellIndex > 0)
            {
                AppendText(" | ", wide);
            }

            foreach (var content in cell)
            {
                if (content is LeafBlock leaf)
                {
                    RenderInlines(leaf.Inline, !wide);
                }
            }

            if (!wide)
            {
                Close();
            }

            cellIndex++;
        }

        if (!wide)
        {
            Close();
        }
    }

    private void RenderInlines(ContainerInline? container, bool formatted = true)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendText(literal.Content.ToString());
                    break;
                case CodeInline code:
                    if (formatted)
                    {
                        Open("<code>", "</code>", false);
                    }

                    AppendText(code.Content);
                    if (formatted)
                    {
                        Close();
                    }

                    break;
                case EmphasisInline emphasis:
                    var tag = emphasis.DelimiterChar switch
                    {
                        '~' => "s",
                        '_' when emphasis.DelimiterCount == 2 => "u",
                        _ when emphasis.DelimiterCount >= 2 => "b",
                        _ => "i"
                    };
                    if (formatted)
                    {
                        Open($"<{tag}>", $"</{tag}>", false);
                    }

                    RenderInlines(emphasis, formatted);
                    if (formatted)
                    {
                        Close();
                    }

                    break;
                case LinkInline link:
                    RenderLink(link, formatted);
                    break;
                case AutolinkInline autolink:
                    if (formatted)
                    {
                        var url = autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url;
                        Open($"<a href=\"{WebUtility.HtmlEncode(url)}\">", "</a>", false);
                    }

                    AppendText(autolink.Url);
                    if (formatted)
                    {
                        Close();
                    }

                    break;
                case SpoilerInline spoiler:
                    if (formatted)
                    {
                        Open("<tg-spoiler>", "</tg-spoiler>", false);
                    }

                    AppendText(spoiler.Content.ToString());
                    if (formatted)
                    {
                        Close();
                    }

                    break;
                case LineBreakInline:
                    AppendText("\n");
                    break;
                case HtmlEntityInline entity:
                    AppendText(entity.Transcoded.ToString());
                    break;
                case HtmlInline html:
                    AppendText(html.Tag);
                    break;
                case TaskList task:
                    AppendText(task.Checked ? "☑ " : "☐ ");
                    break;
                case ContainerInline nested:
                    RenderInlines(nested, formatted);
                    break;
            }
        }
    }

    private void RenderLink(LinkInline link, bool formatted)
    {
        var url = link.Url ?? string.Empty;
        var measured = new TelegramRichTextRenderer(int.MaxValue, measureOnly: true);
        if (link.FirstChild is null)
        {
            measured.AppendText(url);
        }
        else
        {
            measured.RenderInlines(link, false);
        }

        var label = measured._text.ToString();
        var suffix = string.IsNullOrEmpty(url) || string.Equals(label, url, StringComparison.Ordinal) ? string.Empty : $" ({url})";
        var text = label + suffix;
        if (!formatted || string.IsNullOrWhiteSpace(label) || _openTags.Count >= FormattingDepthLimit || text.Length > _partLengthLimit)
        {
            // Без тега ссылки адрес должен быть виден и в HTML. Слишком длинные ссылки
            // режем как обычный текст, чтобы не создавать части с одним скрытым URL.
            AppendText(text);
            return;
        }

        // Подпись и адрес должны попасть в одну часть: повторная отправка любой части
        // и запись истории сохраняют URL, даже если в HTML видна только подпись.
        if (text.Length > _partLengthLimit - _text.Length)
        {
            FinishPart();
        }

        Open($"<a href=\"{WebUtility.HtmlEncode(url)}\">", "</a>", false);
        if (link.FirstChild is null)
        {
            AppendText(url);
        }
        else
        {
            RenderInlines(link);
        }

        Close();
        AppendText(suffix, false);
    }

    private void Open(string start, string end, bool block = true, int precedingCells = 0, int followingCells = 0)
    {
        if (!_measureOnly && block && _blockCount >= BlockCountLimit && _text.Length > 0)
        {
            FinishPart();
        }

        var suppressed = _openTags.Count >= FormattingDepthLimit;
        var tag = suppressed ? new OpenTag(string.Empty, string.Empty, false) : new OpenTag(start, end, block, precedingCells, followingCells);
        _requiresPlainText |= suppressed;
        _html.Append(tag.Start);
        _openTags.Add(tag);
        if (tag.Block)
        {
            _blockCount++;
        }
    }

    private void Close()
    {
        var tag = _openTags[^1];
        _html.Append(tag.End);
        _openTags.RemoveAt(_openTags.Count - 1);
    }

    private void AppendSeparator(int newlines)
    {
        if (_hasText && _trailingNewlines < newlines)
        {
            AppendText(new string('\n', newlines - _trailingNewlines), false);
        }
    }

    private void AppendText(string text, bool renderHtml = true)
    {
        while (text.Length > 0)
        {
            var capacity = _partLengthLimit - _text.Length;
            if (capacity == 0)
            {
                FinishPart();
                continue;
            }

            var length = GetPartLength(text, capacity);
            if (length == 0)
            {
                FinishPart();
                continue;
            }

            var piece = text[..length];
            _text.Append(piece);
            if (renderHtml)
            {
                var encoded = WebUtility.HtmlEncode(piece);
                _html.Append(_preformatted ? encoded : encoded.Replace("\n", "<br/>", StringComparison.Ordinal));
            }

            foreach (var character in piece)
            {
                _trailingNewlines = character == '\n' ? _trailingNewlines + 1 : 0;
            }

            _hasText = true;
            text = text[length..];
            if (text.Length > 0)
            {
                FinishPart();
            }
        }
    }

    private void PrepareTextBoundaries(string text)
    {
        // Графема может проходить через несколько Markdown-узлов (например, e&#x301;).
        // Границы считаем по всему тексту истории до разбиения и открытия HTML-тегов.
        var boundaries = new List<int> { 0 };
        var offset = 0;
        while (offset < text.Length)
        {
            var remaining = StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            while (remaining > _partLengthLimit)
            {
                // Даже чрезмерно длинная графема должна помещаться в поле БД.
                var length = char.IsHighSurrogate(text[offset + _partLengthLimit - 1]) ? _partLengthLimit - 1 : _partLengthLimit;
                offset += length;
                remaining -= length;
                boundaries.Add(offset);
            }

            offset += remaining;
            boundaries.Add(offset);
        }

        _textBoundaries = [.. boundaries];
    }

    private int GetPartLength(string text, int capacity)
    {
        if (_measureOnly)
        {
            return Math.Min(text.Length, capacity);
        }

        var offset = _textOffset + _text.Length;
        var boundaryIndex = Array.BinarySearch(_textBoundaries, offset + capacity);
        if (boundaryIndex < 0)
        {
            boundaryIndex = ~boundaryIndex - 1;
        }

        var length = Math.Min(text.Length, Math.Max(0, _textBoundaries[boundaryIndex] - offset));
        if (length > 0 && length < text.Length)
        {
            var boundary = text.LastIndexOfAny(['\n', ' ', '\t'], length - 1, length);
            if (boundary >= length / 2 && Array.BinarySearch(_textBoundaries, offset + boundary + 1) >= 0)
            {
                length = boundary + 1;
            }
        }

        return length;
    }

    private void FinishPart()
    {
        var text = _text.ToString();
        if (text.Length > 0)
        {
            for (var i = _openTags.Count - 1; i >= 0; i--)
            {
                var tag = _openTags[i];
                _html.Append(tag.End);
                AppendEmptyCells(tag, tag.FollowingCells);
            }

            // При ограничении глубины исчезают границы блоков HTML. Текст сохраняет
            // разделители, маркеры списков и URL, поэтому такую часть уплощаем целиком.
            var html = _requiresPlainText
                ? $"<p>{WebUtility.HtmlEncode(text).Replace("\n", "<br/>", StringComparison.Ordinal)}</p>"
                : _html.ToString();
            _parts.Add(new(text, html));
        }

        _textOffset += text.Length;
        _text.Clear();
        _html.Clear();
        _blockCount = 0;
        _requiresPlainText = _openTags.Count > FormattingDepthLimit;
        // При разрезании абзаца, ссылки или кода закрываем теги в предыдущей части и открываем
        // заново в следующей: ни один запрос к Telegram не содержит незаконченной разметки.
        foreach (var tag in _openTags)
        {
            // Длинная ячейка может продолжиться в следующей части: пустые соседи удерживают
            // её в исходной колонке и сохраняют число колонок обеих половин строки.
            AppendEmptyCells(tag, tag.PrecedingCells);
            _html.Append(tag.Start);
            if (tag.Block)
            {
                _blockCount += tag.PrecedingCells + 1;
            }
        }
    }

    private void AppendEmptyCells(OpenTag tag, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _html.Append(tag.Start).Append(tag.End);
        }
    }

    private sealed class OpenTag(string start, string end, bool block, int precedingCells = 0, int followingCells = 0)
    {
        public string Start { get; } = start;

        public string End { get; } = end;

        public bool Block { get; } = block;

        public int PrecedingCells { get; } = precedingCells;

        public int FollowingCells { get; } = followingCells;
    }
}

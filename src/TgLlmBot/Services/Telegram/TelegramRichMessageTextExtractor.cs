using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Telegram.Bot.Types;

namespace TgLlmBot.Services.Telegram;

internal static class TelegramRichMessageTextExtractor
{
    public static string Extract(RichMessage message, bool isOwnBotMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        var blocks = message.Blocks;
        // Только единственный внешний details нашего бота может быть служебной оболочкой.
        // Заголовки чужих сообщений и вложенных блоков остаются частью контекста.
        if (isOwnBotMessage && blocks is [RichBlockDetails details] && IsExpansionSummary(ExtractText(details.Summary)))
        {
            blocks = details.Blocks;
        }

        return ExtractBlocks(blocks).Trim();
    }

    private static bool IsExpansionSummary(string summary)
    {
        if (string.Equals(summary, "Развернуть ответ", StringComparison.Ordinal))
        {
            return true;
        }

        const string prefix = "Развернуть ответ · ";
        if (!summary.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var numbers = summary.AsSpan(prefix.Length);
        var separator = numbers.IndexOf('/');
        return separator > 0
            && int.TryParse(numbers[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var part)
            && int.TryParse(numbers[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            && total > 1 && part > 0 && part <= total;
    }

    private static string ExtractBlocks(IEnumerable<RichBlock> blocks)
    {
        return string.Join('\n', blocks.Select(ExtractBlock).Where(static text => !string.IsNullOrEmpty(text)));
    }

    private static string ExtractBlock(RichBlock block)
    {
        return block switch
        {
            RichBlockParagraph paragraph => ExtractText(paragraph.Text),
            RichBlockSectionHeading heading => ExtractText(heading.Text),
            RichBlockPreformatted preformatted => ExtractText(preformatted.Text),
            RichBlockFooter footer => ExtractText(footer.Text),
            RichBlockDetails details => ExtractDetails(details),
            RichBlockBlockQuotation quote => ExtractBlocks(quote.Blocks),
            RichBlockExpandableBlockQuotation quote => ExtractText(quote.Text),
            RichBlockPullQuotation quote => ExtractText(quote.Text),
            RichBlockList list => string.Join('\n', list.Items.Select(static item => ExtractBlocks(item.Blocks))),
            RichBlockTable table => string.Join('\n', table.Cells.Select(static row => string.Join(" | ", row.Select(static cell => ExtractText(cell.Text))))),
            RichBlockMathematicalExpression expression => expression.Expression,
            RichBlockDivider => "───",
            _ => string.Empty
        };
    }

    private static string ExtractDetails(RichBlockDetails details)
    {
        var summary = ExtractText(details.Summary);
        var content = ExtractBlocks(details.Blocks);
        if (string.IsNullOrEmpty(summary))
        {
            return content;
        }

        return string.IsNullOrEmpty(content) ? summary : $"{summary}\n{content}";
    }

    private static string ExtractLink(RichTextUrl link)
    {
        var label = ExtractText(link.Text);
        if (string.IsNullOrWhiteSpace(label))
        {
            return link.Url;
        }

        return string.IsNullOrEmpty(link.Url) || string.Equals(label, link.Url, StringComparison.Ordinal)
            ? label
            : $"{label} ({link.Url})";
    }

    private static string ExtractText(RichText? text)
    {
        return text switch
        {
            RichTextText literal => literal.Text,
            RichTextArray array => string.Concat(array.Array.Select(ExtractText)),
            RichTextBold bold => ExtractText(bold.Text),
            RichTextItalic italic => ExtractText(italic.Text),
            RichTextUnderline underline => ExtractText(underline.Text),
            RichTextStrikethrough strikethrough => ExtractText(strikethrough.Text),
            RichTextSpoiler spoiler => ExtractText(spoiler.Text),
            RichTextCode code => ExtractText(code.Text),
            RichTextUrl url => ExtractLink(url),
            RichTextTextMention mention => ExtractText(mention.Text),
            RichTextMention mention => ExtractText(mention.Text),
            RichTextDateTime dateTime => ExtractText(dateTime.Text),
            RichTextSubscript subscript => ExtractText(subscript.Text),
            RichTextSuperscript superscript => ExtractText(superscript.Text),
            RichTextMarked marked => ExtractText(marked.Text),
            RichTextEmailAddress email => ExtractText(email.Text),
            RichTextPhoneNumber phone => ExtractText(phone.Text),
            RichTextBankCardNumber card => ExtractText(card.Text),
            RichTextHashtag hashtag => ExtractText(hashtag.Text),
            RichTextCashtag cashtag => ExtractText(cashtag.Text),
            RichTextBotCommand command => ExtractText(command.Text),
            RichTextAnchorLink anchor => ExtractText(anchor.Text),
            RichTextReference reference => ExtractText(reference.Text),
            RichTextReferenceLink reference => ExtractText(reference.Text),
            RichTextButton button => ExtractText(button.Button.Text),
            RichTextCustomEmoji emoji => emoji.AlternativeText,
            RichTextMathematicalExpression expression => expression.Expression,
            _ => string.Empty
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types;

namespace TgLlmBot.Services.Telegram;

internal static class TelegramRichMessageTextExtractor
{
    public static string Extract(RichMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return ExtractBlocks(message.Blocks).Trim();
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
            // Summary у ответов бота служит кнопкой «Развернуть ответ», а не частью ответа.
            RichBlockDetails details => ExtractBlocks(details.Blocks),
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
            RichTextUrl url => ExtractText(url.Text),
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

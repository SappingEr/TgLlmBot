namespace TgLlmBot.Commands.ChatWithLlm.Services;

public interface ILlmResponseFormatter
{
    LlmResponsePart[] Format(string markdown);

    LlmResponsePart[] FormatPlainText(string text);
}

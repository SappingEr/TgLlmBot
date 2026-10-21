using System;

namespace TgLlmBot.Commands.ChatWithLlm.Services;

public class DefaultLlmResponseFormatterOptions
{
    public DefaultLlmResponseFormatterOptions(bool collapseLongResponses, int characterThreshold, int lineThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(characterThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineThreshold, 1);
        CollapseLongResponses = collapseLongResponses;
        CharacterThreshold = characterThreshold;
        LineThreshold = lineThreshold;
    }

    public bool CollapseLongResponses { get; }

    public int CharacterThreshold { get; }

    public int LineThreshold { get; }
}

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace TgLlmBot.Configuration.Options.Telegram;

[SuppressMessage("ReSharper", "PreferConcreteValueOverDefault")]
public class TelegramOptions
{
    [Required]
    [MaxLength(10000)]
    public string BotToken { get; set; } = default!;

    [Required]
    [MinLength(1)]
    public long[] AllowedChatIds { get; set; } = default!;

    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public string BotName { get; set; } = default!;

    public bool CollapseLongResponses { get; set; } = true;

    [Range(1, int.MaxValue)]
    public int ResponseCollapseCharacterThreshold { get; set; } = 600;

    [Range(1, int.MaxValue)]
    public int ResponseCollapseLineThreshold { get; set; } = 8;
}

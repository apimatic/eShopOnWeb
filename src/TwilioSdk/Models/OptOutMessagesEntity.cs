using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record OptOutMessagesEntity
{
    /// <summary>
    /// Combination of KeywordType and Locale in format: {KeywordType}.{Locale}
    /// <para>
    /// Valid KeywordTypes: STOP, START, HELP
    /// Valid Locales: See LocaleEnum for full list
    /// </para>
    /// <para>
    /// Examples: STOP.ENGLISH, START.SPANISH, HELP.FRENCH
    /// </para>
    /// </summary>
    [JsonPropertyName("keywordType")]
    [RegularExpression("^(STOP|START|HELP)\\.(ALL|AFRIKAANS|ARABIC|BENGALI|CHINESE|CROATIAN|CZECH|DANISH|DUTCH|ENGLISH|ESTONIAN|FINNISH|FRENCH|GERMAN|GREEK|HEBREW|HINDI|HUNGARIAN|ITALIAN|JAPANESE|KOREAN|LATVIAN|LITHUANIAN|MALAY|MALAYSIAN|NORWEGIAN|POLISH|PORTUGUESE|RUSSIAN|SLOVAK|SLOVENE|SPANISH|SOUTHERN_NDEBELE|SOUTHERN_SOTHO|SWATI|SWEDISH|TAMIL|TSWANA|TSONGA|VENDA|XHOSA|ZULU)$")]
    public required string KeywordType { get; init; }

    /// <summary>
    /// Message type (typically country code or region identifier)
    /// </summary>
    [JsonPropertyName("messageType")]
    public required string MessageType { get; init; }

    /// <summary>
    /// The message text content (max 320 characters)
    /// </summary>
    [JsonPropertyName("message")]
    [StringLength(320, MinimumLength = 1)]
    public required string Message { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}

using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The language key/identifier (typically uppercase)
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Key>))]
public sealed record Key : StringEnum<Key>
{
    private Key(string value) : base(value)
    {
    }

    public static readonly Key All = new("ALL");

    public static readonly Key Afrikaans = new("AFRIKAANS");

    public static readonly Key Arabic = new("ARABIC");

    public static readonly Key Bengali = new("BENGALI");

    public static readonly Key Chinese = new("CHINESE");

    public static readonly Key Croatian = new("CROATIAN");

    public static readonly Key Czech = new("CZECH");

    public static readonly Key Danish = new("DANISH");

    public static readonly Key Dutch = new("DUTCH");

    public static readonly Key English = new("ENGLISH");

    public static readonly Key Estonian = new("ESTONIAN");

    public static readonly Key Finnish = new("FINNISH");

    public static readonly Key French = new("FRENCH");

    public static readonly Key German = new("GERMAN");

    public static readonly Key Greek = new("GREEK");

    public static readonly Key Hebrew = new("HEBREW");

    public static readonly Key Hindi = new("HINDI");

    public static readonly Key Hungarian = new("HUNGARIAN");

    public static readonly Key Italian = new("ITALIAN");

    public static readonly Key Japanese = new("JAPANESE");

    public static readonly Key Korean = new("KOREAN");

    public static readonly Key Latvian = new("LATVIAN");

    public static readonly Key Lithuanian = new("LITHUANIAN");

    public static readonly Key Malay = new("MALAY");

    public static readonly Key Malaysian = new("MALAYSIAN");

    public static readonly Key Norwegian = new("NORWEGIAN");

    public static readonly Key Polish = new("POLISH");

    public static readonly Key Portuguese = new("PORTUGUESE");

    public static readonly Key Russian = new("RUSSIAN");

    public static readonly Key Slovak = new("SLOVAK");

    public static readonly Key Slovene = new("SLOVENE");

    public static readonly Key Spanish = new("SPANISH");

    public static readonly Key SouthernNdebele = new("SOUTHERN_NDEBELE");

    public static readonly Key SouthernSotho = new("SOUTHERN_SOTHO");

    public static readonly Key Swati = new("SWATI");

    public static readonly Key Swedish = new("SWEDISH");

    public static readonly Key Tamil = new("TAMIL");

    public static readonly Key Tswana = new("TSWANA");

    public static readonly Key Tsonga = new("TSONGA");

    public static readonly Key Venda = new("VENDA");

    public static readonly Key Xhosa = new("XHOSA");

    public static readonly Key Zulu = new("ZULU");

    public static Key FromValue(string value) => FromValueCore(value);
}

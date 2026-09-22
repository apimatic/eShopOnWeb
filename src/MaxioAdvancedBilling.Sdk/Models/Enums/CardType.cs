using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The type of card used.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardType>))]
public sealed record CardType : StringEnum<CardType>
{
    private CardType(string value) : base(value)
    {
    }

    public static readonly CardType Visa = new("visa");

    public static readonly CardType Master = new("master");

    public static readonly CardType Elo = new("elo");

    public static readonly CardType Cabal = new("cabal");

    public static readonly CardType Alelo = new("alelo");

    public static readonly CardType Discover = new("discover");

    public static readonly CardType AmericanExpress = new("american_express");

    public static readonly CardType Naranja = new("naranja");

    public static readonly CardType DinersClub = new("diners_club");

    public static readonly CardType Jcb = new("jcb");

    public static readonly CardType Dankort = new("dankort");

    public static readonly CardType Maestro = new("maestro");

    public static readonly CardType MaestroNoLuhn = new("maestro_no_luhn");

    public static readonly CardType Forbrugsforeningen = new("forbrugsforeningen");

    public static readonly CardType Sodexo = new("sodexo");

    public static readonly CardType Alia = new("alia");

    public static readonly CardType Vr = new("vr");

    public static readonly CardType Unionpay = new("unionpay");

    public static readonly CardType Carnet = new("carnet");

    public static readonly CardType CartesBancaires = new("cartes_bancaires");

    public static readonly CardType Olimpica = new("olimpica");

    public static readonly CardType Creditel = new("creditel");

    public static readonly CardType Confiable = new("confiable");

    public static readonly CardType Synchrony = new("synchrony");

    public static readonly CardType Routex = new("routex");

    public static readonly CardType Mada = new("mada");

    public static readonly CardType BpPlus = new("bp_plus");

    public static readonly CardType Passcard = new("passcard");

    public static readonly CardType Edenred = new("edenred");

    public static readonly CardType Anda = new("anda");

    public static readonly CardType TarjetaD = new("tarjeta-d");

    public static readonly CardType Hipercard = new("hipercard");

    public static readonly CardType Bogus = new("bogus");

    public static readonly CardType Switch = new("switch");

    public static readonly CardType Solo = new("solo");

    public static readonly CardType Laser = new("laser");

    public static CardType FromValue(string value) => FromValueCore(value);
}

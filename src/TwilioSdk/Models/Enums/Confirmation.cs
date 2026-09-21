using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Whether to prompt the caller to confirm their payment information before submitting to the payment gateway. If <c>true</c>, the caller will hear the last 4 digits of their card or account number and must press 1 to confirm or 2 to cancel. Default is <c>false</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Confirmation>))]
public sealed record Confirmation : StringEnum<Confirmation>
{
    private Confirmation(string value) : base(value)
    {
    }

    public static readonly Confirmation True = new("true");

    public static readonly Confirmation False = new("false");

    public static Confirmation FromValue(string value) => FromValueCore(value);
}

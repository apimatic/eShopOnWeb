using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The tokenization method that generated the ID.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<TokenType>))]
public sealed record TokenType : StringEnum<TokenType>
{
    private TokenType(string value) : base(value)
    {
    }

    /// <summary>
    /// The PayPal billing agreement ID. References an approved recurring payment for goods or services.
    /// </summary>
    public static readonly TokenType BillingAgreement = new("BILLING_AGREEMENT");

    public static TokenType FromValue(string value) => FromValueCore(value);
}

using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Electronic Commerce Indicator (ECI). The ECI value is part of the 2 data elements that indicate the transaction was processed electronically. This should be passed on the authorization transaction to the Gateway/Processor.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EciFlag>))]
public sealed record EciFlag : StringEnum<EciFlag>
{
    private EciFlag(string value) : base(value)
    {
    }

    /// <summary>
    /// Mastercard non-3-D Secure transaction.
    /// </summary>
    public static readonly EciFlag MastercardNon3DSecureTransaction = new("MASTERCARD_NON_3D_SECURE_TRANSACTION");

    /// <summary>
    /// Mastercard attempted authentication transaction.
    /// </summary>
    public static readonly EciFlag MastercardAttemptedAuthenticationTransaction = new("MASTERCARD_ATTEMPTED_AUTHENTICATION_TRANSACTION");

    /// <summary>
    /// Mastercard fully authenticated transaction.
    /// </summary>
    public static readonly EciFlag MastercardFullyAuthenticatedTransaction = new("MASTERCARD_FULLY_AUTHENTICATED_TRANSACTION");

    /// <summary>
    /// VISA, AMEX, JCB, DINERS CLUB fully authenticated transaction.
    /// </summary>
    public static readonly EciFlag FullyAuthenticatedTransaction = new("FULLY_AUTHENTICATED_TRANSACTION");

    /// <summary>
    /// VISA, AMEX, JCB, DINERS CLUB attempted authentication transaction.
    /// </summary>
    public static readonly EciFlag AttemptedAuthenticationTransaction = new("ATTEMPTED_AUTHENTICATION_TRANSACTION");

    /// <summary>
    /// VISA, AMEX, JCB, DINERS CLUB non-3-D Secure transaction.
    /// </summary>
    public static readonly EciFlag Non3DSecureTransaction = new("NON_3D_SECURE_TRANSACTION");

    public static EciFlag FromValue(string value) => FromValueCore(value);
}

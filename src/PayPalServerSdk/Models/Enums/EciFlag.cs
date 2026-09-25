using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Electronic Commerce Indicator (ECI). The ECI value is part of the 2 data elements that indicate the transaction was processed electronically. This should be passed on the authorization transaction to the Gateway/Processor.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EciFlag>))]
public sealed record EciFlag : OpenStringEnum<EciFlag>
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

    public TResult Match<TResult>(Func<TResult> onMastercardNon3DSecureTransaction,
        Func<TResult> onMastercardAttemptedAuthenticationTransaction,
        Func<TResult> onMastercardFullyAuthenticatedTransaction,
        Func<TResult> onFullyAuthenticatedTransaction,
        Func<TResult> onAttemptedAuthenticationTransaction,
        Func<TResult> onNon3DSecureTransaction,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == MastercardNon3DSecureTransaction => onMastercardNon3DSecureTransaction(),
            _ when this ==
                MastercardAttemptedAuthenticationTransaction => onMastercardAttemptedAuthenticationTransaction(),
            _ when this == MastercardFullyAuthenticatedTransaction => onMastercardFullyAuthenticatedTransaction(),
            _ when this == FullyAuthenticatedTransaction => onFullyAuthenticatedTransaction(),
            _ when this == AttemptedAuthenticationTransaction => onAttemptedAuthenticationTransaction(),
            _ when this == Non3DSecureTransaction => onNon3DSecureTransaction(),
            _ => otherwise(Value)
        };

    public void Match(Action onMastercardNon3DSecureTransaction,
        Action onMastercardAttemptedAuthenticationTransaction,
        Action onMastercardFullyAuthenticatedTransaction,
        Action onFullyAuthenticatedTransaction,
        Action onAttemptedAuthenticationTransaction,
        Action onNon3DSecureTransaction,
        Action<string> otherwise)
    {
        if (this == MastercardNon3DSecureTransaction) onMastercardNon3DSecureTransaction();
        else if (this == MastercardAttemptedAuthenticationTransaction) onMastercardAttemptedAuthenticationTransaction();
        else if (this == MastercardFullyAuthenticatedTransaction) onMastercardFullyAuthenticatedTransaction();
        else if (this == FullyAuthenticatedTransaction) onFullyAuthenticatedTransaction();
        else if (this == AttemptedAuthenticationTransaction) onAttemptedAuthenticationTransaction();
        else if (this == Non3DSecureTransaction) onNon3DSecureTransaction();
        else otherwise(Value);
    }
}

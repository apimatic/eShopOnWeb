using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status for the authorized payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthorizationStatus>))]
public sealed record AuthorizationStatus : OpenStringEnum<AuthorizationStatus>
{
    private AuthorizationStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The authorized payment is created. No captured payments have been made for this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Created = new("CREATED");

    /// <summary>
    /// The authorized payment has one or more captures against it. The sum of these captured payments is greater than the amount of the original authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Captured = new("CAPTURED");

    /// <summary>
    /// PayPal cannot authorize funds for this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Denied = new("DENIED");

    /// <summary>
    /// A captured payment was made for the authorized payment for an amount that is less than the amount of the original authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus PartiallyCaptured = new("PARTIALLY_CAPTURED");

    /// <summary>
    /// The authorized payment was voided. No more captured payments can be made against this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Voided = new("VOIDED");

    /// <summary>
    /// The created authorization is in pending state. For more information, see status.details.
    /// </summary>
    public static readonly AuthorizationStatus Pending = new("PENDING");

    public TResult Match<TResult>(Func<TResult> onCreated,
        Func<TResult> onCaptured,
        Func<TResult> onDenied,
        Func<TResult> onPartiallyCaptured,
        Func<TResult> onVoided,
        Func<TResult> onPending,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Created => onCreated(),
            _ when this == Captured => onCaptured(),
            _ when this == Denied => onDenied(),
            _ when this == PartiallyCaptured => onPartiallyCaptured(),
            _ when this == Voided => onVoided(),
            _ when this == Pending => onPending(),
            _ => otherwise(Value)
        };

    public void Match(Action onCreated,
        Action onCaptured,
        Action onDenied,
        Action onPartiallyCaptured,
        Action onVoided,
        Action onPending,
        Action<string> otherwise)
    {
        if (this == Created) onCreated();
        else if (this == Captured) onCaptured();
        else if (this == Denied) onDenied();
        else if (this == PartiallyCaptured) onPartiallyCaptured();
        else if (this == Voided) onVoided();
        else if (this == Pending) onPending();
        else otherwise(Value);
    }
}

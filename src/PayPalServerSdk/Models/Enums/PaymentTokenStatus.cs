using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the payment token.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaymentTokenStatus>))]
public sealed record PaymentTokenStatus : OpenStringEnum<PaymentTokenStatus>
{
    private PaymentTokenStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// A setup token is initialized with minimal information, more data must be added to the setup-token to be vaulted
    /// </summary>
    public static readonly PaymentTokenStatus Created = new("CREATED");

    /// <summary>
    /// A contingency on payer approval is required before the payment method can be saved.
    /// </summary>
    public static readonly PaymentTokenStatus PayerActionRequired = new("PAYER_ACTION_REQUIRED");

    /// <summary>
    /// Setup token is ready to be vaulted. If a buyer approval contigency was returned, it is has been approved.
    /// </summary>
    public static readonly PaymentTokenStatus Approved = new("APPROVED");

    /// <summary>
    /// The payment token has been vaulted.
    /// </summary>
    public static readonly PaymentTokenStatus Vaulted = new("VAULTED");

    /// <summary>
    /// A vaulted payment method token has been tokenized for short term (one time) use.
    /// </summary>
    public static readonly PaymentTokenStatus Tokenized = new("TOKENIZED");

    public TResult Match<TResult>(Func<TResult> onCreated,
        Func<TResult> onPayerActionRequired,
        Func<TResult> onApproved,
        Func<TResult> onVaulted,
        Func<TResult> onTokenized,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Created => onCreated(),
            _ when this == PayerActionRequired => onPayerActionRequired(),
            _ when this == Approved => onApproved(),
            _ when this == Vaulted => onVaulted(),
            _ when this == Tokenized => onTokenized(),
            _ => otherwise(Value)
        };

    public void Match(Action onCreated,
        Action onPayerActionRequired,
        Action onApproved,
        Action onVaulted,
        Action onTokenized,
        Action<string> otherwise)
    {
        if (this == Created) onCreated();
        else if (this == PayerActionRequired) onPayerActionRequired();
        else if (this == Approved) onApproved();
        else if (this == Vaulted) onVaulted();
        else if (this == Tokenized) onTokenized();
        else otherwise(Value);
    }
}

using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The action to take on the subscription if the initial payment for the setup fails.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SetupFeeFailureAction>))]
public sealed record SetupFeeFailureAction : OpenStringEnum<SetupFeeFailureAction>
{
    private SetupFeeFailureAction(string value) : base(value)
    {
    }

    /// <summary>
    /// Continues the subscription if the initial payment for the setup fails.
    /// </summary>
    public static readonly SetupFeeFailureAction Continue = new("CONTINUE");

    /// <summary>
    /// Cancels the subscription if the initial payment for the setup fails.
    /// </summary>
    public static readonly SetupFeeFailureAction Cancel = new("CANCEL");

    public TResult Match<TResult>(Func<TResult> onContinue, Func<TResult> onCancel, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Continue => onContinue(),
            _ when this == Cancel => onCancel(),
            _ => otherwise(Value)
        };

    public void Match(Action onContinue, Action onCancel, Action<string> otherwise)
    {
        if (this == Continue) onContinue();
        else if (this == Cancel) onCancel();
        else otherwise(Value);
    }
}

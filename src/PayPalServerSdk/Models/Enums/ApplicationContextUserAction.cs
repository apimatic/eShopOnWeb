using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Configures the label name to <c>Continue</c> or <c>Subscribe Now</c> for subscription consent experience.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ApplicationContextUserAction>))]
public sealed record ApplicationContextUserAction : OpenStringEnum<ApplicationContextUserAction>
{
    private ApplicationContextUserAction(string value) : base(value)
    {
    }

    /// <summary>
    /// After you redirect the customer to the PayPal subscription consent page, a Continue button appears. Use this option when you want to control the activation of the subscription and do not want PayPal to activate the subscription.
    /// </summary>
    public static readonly ApplicationContextUserAction Continue = new("CONTINUE");

    /// <summary>
    /// After you redirect the customer to the PayPal subscription consent page, a Subscribe Now button appears. Use this option when you want PayPal to activate the subscription.
    /// </summary>
    public static readonly ApplicationContextUserAction SubscribeNow = new("SUBSCRIBE_NOW");

    public TResult Match<TResult>(Func<TResult> onContinue,
        Func<TResult> onSubscribeNow,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Continue => onContinue(),
            _ when this == SubscribeNow => onSubscribeNow(),
            _ => otherwise(Value)
        };

    public void Match(Action onContinue, Action onSubscribeNow, Action<string> otherwise)
    {
        if (this == Continue) onContinue();
        else if (this == SubscribeNow) onSubscribeNow();
        else otherwise(Value);
    }
}

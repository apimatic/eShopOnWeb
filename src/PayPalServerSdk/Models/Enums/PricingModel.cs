using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The pricing model for the billing cycle.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PricingModel>))]
public sealed record PricingModel : OpenStringEnum<PricingModel>
{
    private PricingModel(string value) : base(value)
    {
    }

    /// <summary>
    /// A fixed pricing scheme where the customer is charged a fixed amount.
    /// </summary>
    public static readonly PricingModel Fixed = new("FIXED");

    /// <summary>
    /// A variable pricing scheme where the customer is charged a variable amount.
    /// </summary>
    public static readonly PricingModel Variable = new("VARIABLE");

    /// <summary>
    /// A auto-reload pricing scheme where the customer is charged a fixed amount for reload.
    /// </summary>
    public static readonly PricingModel AutoReload = new("AUTO_RELOAD");

    public TResult Match<TResult>(Func<TResult> onFixed,
        Func<TResult> onVariable,
        Func<TResult> onAutoReload,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Fixed => onFixed(),
            _ when this == Variable => onVariable(),
            _ when this == AutoReload => onAutoReload(),
            _ => otherwise(Value)
        };

    public void Match(Action onFixed, Action onVariable, Action onAutoReload, Action<string> otherwise)
    {
        if (this == Fixed) onFixed();
        else if (this == Variable) onVariable();
        else if (this == AutoReload) onAutoReload();
        else otherwise(Value);
    }
}

using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The pricing model for the billing cycle.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PricingModel>))]
public sealed record PricingModel : StringEnum<PricingModel>
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

    public static PricingModel FromValue(string value) => FromValueCore(value);
}

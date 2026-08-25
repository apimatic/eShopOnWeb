using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The plan status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionPlanStatus>))]
public sealed record SubscriptionPlanStatus : StringEnum<SubscriptionPlanStatus>
{
    private SubscriptionPlanStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The plan was created. You cannot create subscriptions for a plan in this state.
    /// </summary>
    public static readonly SubscriptionPlanStatus Created = new("CREATED");

    /// <summary>
    /// The plan is inactive.
    /// </summary>
    public static readonly SubscriptionPlanStatus Inactive = new("INACTIVE");

    /// <summary>
    /// The plan is active. You can only create subscriptions for a plan in this state.
    /// </summary>
    public static readonly SubscriptionPlanStatus Active = new("ACTIVE");

    public static SubscriptionPlanStatus FromValue(string value) => FromValueCore(value);
}

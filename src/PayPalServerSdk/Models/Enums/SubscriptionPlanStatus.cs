using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The plan status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionPlanStatus>))]
public sealed record SubscriptionPlanStatus : OpenStringEnum<SubscriptionPlanStatus>
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

    public TResult Match<TResult>(Func<TResult> onCreated,
        Func<TResult> onInactive,
        Func<TResult> onActive,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Created => onCreated(),
            _ when this == Inactive => onInactive(),
            _ when this == Active => onActive(),
            _ => otherwise(Value)
        };

    public void Match(Action onCreated, Action onInactive, Action onActive, Action<string> otherwise)
    {
        if (this == Created) onCreated();
        else if (this == Inactive) onInactive();
        else if (this == Active) onActive();
        else otherwise(Value);
    }
}

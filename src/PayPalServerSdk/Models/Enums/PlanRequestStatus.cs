using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The initial state of the plan. Allowed input values are CREATED and ACTIVE.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PlanRequestStatus>))]
public sealed record PlanRequestStatus : StringEnum<PlanRequestStatus>
{
    private PlanRequestStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The plan was created. You cannot create subscriptions for a plan in this state.
    /// </summary>
    public static readonly PlanRequestStatus Created = new("CREATED");

    /// <summary>
    /// The plan is inactive.
    /// </summary>
    public static readonly PlanRequestStatus Inactive = new("INACTIVE");

    /// <summary>
    /// The plan is active. You can only create subscriptions for a plan in this state.
    /// </summary>
    public static readonly PlanRequestStatus Active = new("ACTIVE");

    public static PlanRequestStatus FromValue(string value) => FromValueCore(value);
}

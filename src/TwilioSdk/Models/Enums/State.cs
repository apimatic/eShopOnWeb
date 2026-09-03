using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The state of the application.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<State>))]
public sealed record State : StringEnum<State>
{
    private State(string value) : base(value)
    {
    }

    public static readonly State Draft = new("DRAFT");

    public static readonly State TwilioReview = new("TWILIO_REVIEW");

    public static readonly State PendingPayment = new("PENDING_PAYMENT");

    public static readonly State PaymentFailed = new("PAYMENT_FAILED");

    public static readonly State InProvisioning = new("IN_PROVISIONING");

    public static readonly State PendingCarrier = new("PENDING_CARRIER");

    public static readonly State Approved = new("APPROVED");

    public static readonly State CorrectionsNeeded = new("CORRECTIONS_NEEDED");

    public static readonly State Canceled = new("CANCELED");

    public static readonly State Archived = new("ARCHIVED");

    public static State FromValue(string value) => FromValueCore(value);
}

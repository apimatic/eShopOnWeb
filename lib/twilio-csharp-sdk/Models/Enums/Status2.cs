using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Status of the Sender ID Registration Application
/// </summary>
[JsonConverter(typeof(StringEnumConverter<Status2>))]
public sealed record Status2 : StringEnum<Status2>
{
    private Status2(string value) : base(value)
    {
    }

    public static readonly Status2 Draft = new("DRAFT");

    public static readonly Status2 PendingReview = new("PENDING_REVIEW");

    public static readonly Status2 InReview = new("IN_REVIEW");

    public static readonly Status2 TwilioApproved = new("TWILIO_APPROVED");

    public static readonly Status2 TwilioRejected = new("TWILIO_REJECTED");

    public static Status2 FromValue(string value) => FromValueCore(value);
}

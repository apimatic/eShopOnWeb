using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The verification status of the Supporting Document resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SupportingDocumentEnumStatus1>))]
public sealed record SupportingDocumentEnumStatus1 : StringEnum<SupportingDocumentEnumStatus1>
{
    private SupportingDocumentEnumStatus1(string value) : base(value)
    {
    }

    public static readonly SupportingDocumentEnumStatus1 Draft = new("DRAFT");

    public static readonly SupportingDocumentEnumStatus1 PendingReview = new("PENDING_REVIEW");

    public static readonly SupportingDocumentEnumStatus1 Rejected = new("REJECTED");

    public static readonly SupportingDocumentEnumStatus1 Approved = new("APPROVED");

    public static readonly SupportingDocumentEnumStatus1 Expired = new("EXPIRED");

    public static readonly SupportingDocumentEnumStatus1 ProvisionallyApproved = new("PROVISIONALLY_APPROVED");

    public static SupportingDocumentEnumStatus1 FromValue(string value) => FromValueCore(value);
}

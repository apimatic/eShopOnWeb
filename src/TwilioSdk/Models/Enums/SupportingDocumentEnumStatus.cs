using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The verification status of the Supporting Document resource.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SupportingDocumentEnumStatus>))]
public sealed record SupportingDocumentEnumStatus : StringEnum<SupportingDocumentEnumStatus>
{
    private SupportingDocumentEnumStatus(string value) : base(value)
    {
    }

    public static readonly SupportingDocumentEnumStatus Draft = new("draft");

    public static readonly SupportingDocumentEnumStatus PendingReview = new("pending-review");

    public static readonly SupportingDocumentEnumStatus Rejected = new("rejected");

    public static readonly SupportingDocumentEnumStatus Approved = new("approved");

    public static readonly SupportingDocumentEnumStatus Expired = new("expired");

    public static readonly SupportingDocumentEnumStatus ProvisionallyApproved = new("provisionally-approved");

    public static SupportingDocumentEnumStatus FromValue(string value) => FromValueCore(value);
}

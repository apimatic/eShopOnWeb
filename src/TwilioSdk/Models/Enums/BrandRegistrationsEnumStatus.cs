using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Brand Registration status. One of "PENDING", "APPROVED", "FAILED", "IN_REVIEW", "DELETION_PENDING", "DELETION_FAILED", "SUSPENDED".
/// </summary>
[JsonConverter(typeof(StringEnumConverter<BrandRegistrationsEnumStatus>))]
public sealed record BrandRegistrationsEnumStatus : StringEnum<BrandRegistrationsEnumStatus>
{
    private BrandRegistrationsEnumStatus(string value) : base(value)
    {
    }

    public static readonly BrandRegistrationsEnumStatus Pending = new("PENDING");

    public static readonly BrandRegistrationsEnumStatus Approved = new("APPROVED");

    public static readonly BrandRegistrationsEnumStatus Failed = new("FAILED");

    public static readonly BrandRegistrationsEnumStatus InReview = new("IN_REVIEW");

    public static readonly BrandRegistrationsEnumStatus DeletionPending = new("DELETION_PENDING");

    public static readonly BrandRegistrationsEnumStatus DeletionFailed = new("DELETION_FAILED");

    public static readonly BrandRegistrationsEnumStatus Suspended = new("SUSPENDED");

    public static BrandRegistrationsEnumStatus FromValue(string value) => FromValueCore(value);
}

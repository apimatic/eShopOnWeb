using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<Status>))]
public sealed record Status : StringEnum<Status>
{
    private Status(string value) : base(value)
    {
    }

    public static readonly Status Draft = new("draft");

    public static readonly Status Scheduled = new("scheduled");

    public static readonly Status Pending = new("pending");

    public static readonly Status Canceled = new("canceled");

    public static readonly Status Active = new("active");

    public static readonly Status Fulfilled = new("fulfilled");

    public static Status FromValue(string value) => FromValueCore(value);
}

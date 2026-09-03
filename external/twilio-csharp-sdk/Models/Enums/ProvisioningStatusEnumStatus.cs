using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Email Provisioning Status
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ProvisioningStatusEnumStatus>))]
public sealed record ProvisioningStatusEnumStatus : StringEnum<ProvisioningStatusEnumStatus>
{
    private ProvisioningStatusEnumStatus(string value) : base(value)
    {
    }

    public static readonly ProvisioningStatusEnumStatus Active = new("active");

    public static readonly ProvisioningStatusEnumStatus InProgress = new("in-progress");

    public static readonly ProvisioningStatusEnumStatus NotConfigured = new("not-configured");

    public static readonly ProvisioningStatusEnumStatus Failed = new("failed");

    public static ProvisioningStatusEnumStatus FromValue(string value) => FromValueCore(value);
}

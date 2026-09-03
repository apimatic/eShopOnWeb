using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Whether the phone number is enabled for emergency calling.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DependentPhoneNumberEnumEmergencyStatus>))]
public sealed record DependentPhoneNumberEnumEmergencyStatus : StringEnum<DependentPhoneNumberEnumEmergencyStatus>
{
    private DependentPhoneNumberEnumEmergencyStatus(string value) : base(value)
    {
    }

    public static readonly DependentPhoneNumberEnumEmergencyStatus Active = new("Active");

    public static readonly DependentPhoneNumberEnumEmergencyStatus Inactive = new("Inactive");

    public static DependentPhoneNumberEnumEmergencyStatus FromValue(string value) => FromValueCore(value);
}

using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of address registration with emergency services. A registered emergency address will be used during handling of emergency calls from this number.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberLocalEnumEmergencyAddressStatus>))]
public sealed record IncomingPhoneNumberLocalEnumEmergencyAddressStatus : StringEnum<IncomingPhoneNumberLocalEnumEmergencyAddressStatus>
{
    private IncomingPhoneNumberLocalEnumEmergencyAddressStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus Registered = new("registered");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus Unregistered = new("unregistered");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus PendingRegistration = new("pending-registration");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus RegistrationFailure = new("registration-failure");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus PendingUnregistration = new("pending-unregistration");

    public static readonly IncomingPhoneNumberLocalEnumEmergencyAddressStatus UnregistrationFailure = new("unregistration-failure");

    public static IncomingPhoneNumberLocalEnumEmergencyAddressStatus FromValue(string value) =>
        FromValueCore(value);
}

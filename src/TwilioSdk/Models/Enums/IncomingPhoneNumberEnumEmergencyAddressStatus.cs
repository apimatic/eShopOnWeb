using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The status of address registration with emergency services. A registered emergency address will be used during handling of emergency calls from this number.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberEnumEmergencyAddressStatus>))]
public sealed record IncomingPhoneNumberEnumEmergencyAddressStatus : StringEnum<IncomingPhoneNumberEnumEmergencyAddressStatus>
{
    private IncomingPhoneNumberEnumEmergencyAddressStatus(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus Registered = new("registered");

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus Unregistered = new("unregistered");

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus PendingRegistration = new("pending-registration");

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus RegistrationFailure = new("registration-failure");

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus PendingUnregistration = new("pending-unregistration");

    public static readonly IncomingPhoneNumberEnumEmergencyAddressStatus UnregistrationFailure = new("unregistration-failure");

    public static IncomingPhoneNumberEnumEmergencyAddressStatus FromValue(string value) => FromValueCore(value);
}

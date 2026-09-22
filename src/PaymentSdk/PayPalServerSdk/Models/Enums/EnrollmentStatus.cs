using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Status of Authentication eligibility.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EnrollmentStatus>))]
public sealed record EnrollmentStatus : StringEnum<EnrollmentStatus>
{
    private EnrollmentStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Yes. The bank is participating in 3-D Secure protocol and will return the ACSUrl.
    /// </summary>
    public static readonly EnrollmentStatus Y = new("Y");

    /// <summary>
    /// No. The bank is not participating in 3-D Secure protocol.
    /// </summary>
    public static readonly EnrollmentStatus N = new("N");

    /// <summary>
    /// Unavailable. The DS or ACS is not available for authentication at the time of the request.
    /// </summary>
    public static readonly EnrollmentStatus U = new("U");

    /// <summary>
    /// Bypass. The merchant authentication rule is triggered to bypass authentication.
    /// </summary>
    public static readonly EnrollmentStatus B = new("B");

    public static EnrollmentStatus FromValue(string value) => FromValueCore(value);
}

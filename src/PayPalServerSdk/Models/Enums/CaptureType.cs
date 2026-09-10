using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The type of capture.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CaptureType>))]
public sealed record CaptureType : StringEnum<CaptureType>
{
    private CaptureType(string value) : base(value)
    {
    }

    /// <summary>
    /// The outstanding balance that the subscriber must clear.
    /// </summary>
    public static readonly CaptureType OutstandingBalance = new("OUTSTANDING_BALANCE");

    public static CaptureType FromValue(string value) => FromValueCore(value);
}

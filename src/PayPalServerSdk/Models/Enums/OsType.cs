using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Operating System type of the device that the buyer is using.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OsType>))]
public sealed record OsType : StringEnum<OsType>
{
    private OsType(string value) : base(value)
    {
    }

    /// <summary>
    /// Google Android OS.
    /// </summary>
    public static readonly OsType Android = new("ANDROID");

    /// <summary>
    /// Apple OS typically found in Apple mobile devices.
    /// </summary>
    public static readonly OsType Ios = new("IOS");

    /// <summary>
    /// Any other OS type.
    /// </summary>
    public static readonly OsType Other = new("OTHER");

    public static OsType FromValue(string value) => FromValueCore(value);
}

using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Authentication Method which is used for the card transaction.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<GooglePayAuthenticationMethod>))]
public sealed record GooglePayAuthenticationMethod : StringEnum<GooglePayAuthenticationMethod>
{
    private GooglePayAuthenticationMethod(string value) : base(value)
    {
    }

    /// <summary>
    /// This authentication method is associated with payment cards stored on file with the user's Google Account. Returned payment data includes primary account number (PAN) with the expiration month and the expiration year.
    /// </summary>
    public static readonly GooglePayAuthenticationMethod PanOnly = new("PAN_ONLY");

    /// <summary>
    /// Returned payment data includes a 3-D Secure (3DS) cryptogram generated on the device. -&gt; If authentication_method=CRYPTOGRAM, it is required that 'cryptogram' parameter in the request has a valid 3-D Secure (3DS) cryptogram generated on the device.
    /// </summary>
    public static readonly GooglePayAuthenticationMethod Cryptogram3Ds = new("CRYPTOGRAM_3DS");

    public static GooglePayAuthenticationMethod FromValue(string value) => FromValueCore(value);
}

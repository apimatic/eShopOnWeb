using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The tokenization method that generated the ID.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultTokenRequestType>))]
public sealed record VaultTokenRequestType : StringEnum<VaultTokenRequestType>
{
    private VaultTokenRequestType(string value) : base(value)
    {
    }

    /// <summary>
    /// The setup token, which is a temporary reference to payment source.
    /// </summary>
    public static readonly VaultTokenRequestType SetupToken = new("SETUP_TOKEN");

    public static VaultTokenRequestType FromValue(string value) => FromValueCore(value);
}

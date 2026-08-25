using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// Merchant provided details of the native app or mobile web browser to facilitate buyer's app switch to the PayPal consumer app.
/// </summary>
public record AppSwitchContext
{
    /// <summary>
    /// Merchant provided, buyer's native app preferences to app switch to the PayPal consumer app.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("native_app")]
    public NativeAppContext? NativeApp { get; init; }

    /// <summary>
    /// Buyer's mobile web browser context to app switch to the PayPal consumer app.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mobile_web")]
    public MobileWebContext? MobileWeb { get; init; }
}

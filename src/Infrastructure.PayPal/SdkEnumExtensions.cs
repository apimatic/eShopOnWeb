using PayPalServerSdk.Core.Enum;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The SDK's enums are StringEnum wrappers whose ToString() includes the type name
/// ("AuthorizationStatus { Value = CREATED }"); the wire value is what we persist and show.
/// </summary>
internal static class SdkEnumExtensions
{
    public static string? WireValue<T>(this StringEnum<T>? value) where T : StringEnum<T>
        => value?.Value;
}

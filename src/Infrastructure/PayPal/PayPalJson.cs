using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Shared JSON options for PayPal wire (de)serialization. Null fields are omitted on write.</summary>
internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}

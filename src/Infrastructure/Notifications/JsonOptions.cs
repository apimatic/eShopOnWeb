using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}

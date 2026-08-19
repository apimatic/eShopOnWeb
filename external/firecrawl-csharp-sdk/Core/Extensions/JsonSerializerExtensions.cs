using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FirecrawlApi.Core.Extensions;

internal static class JsonSerializerExtensions
{
    extension(JsonSerializer)
    {
        public static bool TryDeserialize<T>(JsonElement element, JsonSerializerOptions options,
            [NotNullWhen(true)] out T? result)
        {
            try
            {
                var deserialized = element.Deserialize<T>(options);
                if (deserialized is null)
                {
                    result = default;
                    return false;
                }

                result = deserialized;
                return true;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                result = default;
                return false;
            }
        }
    }
}
using System.Net;
using System.Text.Json;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

/// <summary>Square refused or failed a request. <see cref="Exception.Message"/> is safe to show the operator.</summary>
public sealed class SquareRequestException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>The one place SDK failures become <see cref="SquareRequestException"/>.</summary>
internal static class SquareCall
{
    private const int MaxDetailLength = 300;

    /// <param name="action">What the tool was doing, phrased to follow "to" - e.g. "list the locations".</param>
    public static async Task<T> RunAsync<T>(string action, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            throw new SquareRequestException(
                $"Square refused to {action}: HTTP {(int)ex.StatusCode} {ex.StatusCode}{Describe(ex.Error)}.",
                ex.StatusCode, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw new SquareRequestException(
                $"Square answered the request to {action} (HTTP {(int)ex.StatusCode}) with a response SquareCheck could not read.",
                ex.StatusCode, ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new SquareRequestException($"Square did not answer the request to {action} in time.", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new SquareRequestException($"Could not reach Square to {action}. Check the internet connection.", null, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new SquareRequestException($"Square sign-in could not be used to {action}.", null, ex);
        }
    }

    /// <summary>
    /// The first error Square put in the body (<c>errors[].code</c>/<c>detail</c>), or the OAuth 2.0
    /// <c>error</c>/<c>error_description</c> pair; empty when the body has neither.
    /// </summary>
    internal static string Describe(RawError error)
    {
        try
        {
            // The body may not be JSON at all (a gateway or proxy page), so parse defensively.
            using var document = JsonDocument.Parse(error.ReadAsString());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0 && errors[0].ValueKind == JsonValueKind.Object)
            {
                return Format(Text(errors[0], "code"), Text(errors[0], "detail"));
            }

            return Format(Text(root, "error"), Text(root, "error_description") ?? Text(root, "message"));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Format(string? code, string? detail)
    {
        var parts = new[] { code, detail }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(OneLine).ToArray();
        return parts.Length == 0 ? string.Empty : " - " + string.Join(": ", parts);
    }

    private static string OneLine(string? text)
    {
        // The caller ends the sentence, so drop Square's own closing full stop.
        var flat = string.Join(' ', text!.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        return flat.Length <= MaxDetailLength ? flat : flat[..MaxDetailLength] + "...";
    }
}

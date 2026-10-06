using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// The one place SDK failures become <see cref="SquareIntegrationException"/>. Every Square call
/// in the integration goes through <see cref="RunAsync{T}"/> so each failure kind is handled the
/// same way everywhere. All operations in scope are Case B (<c>ApiException&lt;RawError&gt;</c>).
/// </summary>
public static class SquareCall
{
    public static async Task<T> RunAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> call,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            var errors = ReadErrors(ex.Error);
            var status = ex.StatusCode;
            logger.LogWarning(
                "Square {Operation} returned HTTP {Status}: {Errors}",
                operation, (int)status, string.Join("; ", errors.Select(e => e.ToString())));

            var codes = errors.Select(e => e.Code).Where(c => c.Length > 0).Distinct().ToArray();
            var codeText = codes.Length == 0 ? string.Empty : $" ({string.Join(", ", codes)})";
            var statusCode = (int)status;
            throw statusCode switch
            {
                401 or 403 => new SquareIntegrationException(
                    SquareFailureKind.AuthorizationFailed,
                    $"Square refused the shop's credentials for {operation}{codeText}. Reconnect the Square account.",
                    status, codes, ex),
                429 => new SquareIntegrationException(
                    SquareFailureKind.Unavailable,
                    $"Square is rate limiting {operation}. Try again shortly.", status, codes, ex),
                >= 500 => new SquareIntegrationException(
                    SquareFailureKind.Unavailable,
                    $"Square failed to process {operation}{codeText}.", status, codes, ex),
                _ => new SquareIntegrationException(
                    SquareFailureKind.Rejected,
                    $"Square rejected {operation}{codeText}.", status, codes, ex),
            };
        }
        catch (ResponseDeserializationException ex)
        {
            logger.LogError(ex, "Square {Operation} returned HTTP {Status} with a body that could not be read as {Type}",
                operation, (int)ex.StatusCode, ex.TargetType.Name);
            var success = (int)ex.StatusCode is >= 200 and < 300;
            throw new SquareIntegrationException(
                success ? SquareFailureKind.UnreadableResponse : SquareFailureKind.Rejected,
                $"Square returned a response to {operation} that could not be processed.",
                ex.StatusCode, null, ex);
        }
        catch (SdkTimeoutException ex)
        {
            logger.LogWarning("Square {Operation} received no response within {Timeout}", operation, ex.Timeout);
            throw new SquareIntegrationException(
                SquareFailureKind.Unavailable, $"Square did not answer {operation} in time.", null, null, ex);
        }
        catch (SdkConnectionException ex)
        {
            logger.LogWarning(ex.InnerException, "Square {Operation} could not be reached", operation);
            throw new SquareIntegrationException(
                SquareFailureKind.Unavailable, $"Square could not be reached for {operation}.", null, null, ex);
        }
        catch (AuthSchemeException ex)
        {
            // Our token source raises SquareIntegrationException (e.g. not connected); surface it unchanged.
            var own = ex.SchemeFailures.OfType<SquareIntegrationException>().FirstOrDefault()
                      ?? ex.InnerException as SquareIntegrationException;
            if (own is not null)
            {
                throw own;
            }

            logger.LogError(ex.InnerException, "Square credentials could not be applied for {Operation}", operation);
            throw new SquareIntegrationException(
                SquareFailureKind.AuthorizationFailed,
                $"The shop's Square credentials could not be applied for {operation}.", null, null, ex);
        }
    }

    public static Task RunAsync(
        string operation,
        Func<CancellationToken, Task> call,
        ILogger logger,
        CancellationToken cancellationToken) =>
        RunAsync<bool>(operation, async ct =>
        {
            await call(ct).ConfigureAwait(false);
            return true;
        }, logger, cancellationToken);

    internal static IReadOnlyList<SquareErrorEntry> ReadErrors(RawError error)
    {
        try
        {
            var text = error.ReadAsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<SquareErrorEntry>();
            }

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("errors", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SquareErrorEntry>();
            }

            return list.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new SquareErrorEntry(
                    GetString(e, "category"), GetString(e, "code"), GetString(e, "field"), GetString(e, "detail")))
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<SquareErrorEntry>();
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    internal sealed record SquareErrorEntry(string Category, string Code, string Field, string Detail)
    {
        public override string ToString() =>
            $"{Category}/{Code}" + (Field.Length > 0 ? $" field={Field}" : string.Empty) + (Detail.Length > 0 ? $" detail={Detail}" : string.Empty);
    }
}

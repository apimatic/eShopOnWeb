using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, IEnumerable<FinixError> errors, string? responseBody = null)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors.ToList();
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public IReadOnlyList<FinixError> Errors { get; }
    public string? ResponseBody { get; }

    public bool IsDuplicateIdempotency =>
        Errors.Any(e => e.Message?.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true);

    public string? DuplicateAuthorizationId =>
        Errors.Select(e => e.Authorization).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

    public string? DuplicateTransferId =>
        Errors.Select(e => e.Transfer).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

    private static string BuildMessage(int statusCode, IEnumerable<FinixError> errors)
    {
        var messages = errors
            .Select(e => e.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        return messages.Count > 0
            ? $"Finix request failed ({(int)statusCode}): {string.Join("; ", messages)}"
            : $"Finix request failed with HTTP status {statusCode}.";
    }
}

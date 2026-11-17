using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, IReadOnlyList<FinixErrorDetail> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }
    public IReadOnlyList<FinixErrorDetail> Errors { get; }
    public string? Code => Errors.FirstOrDefault()?.Code;
    public string? LogRef => Errors.FirstOrDefault()?.LogRef;
    public string? TransferId => Errors.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Transfer))?.Transfer;

    private static string BuildMessage(int statusCode, IReadOnlyList<FinixErrorDetail> errors)
    {
        var messages = errors
            .Select(e => e.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        if (messages.Count == 0)
        {
            return $"Finix API request failed with status code {statusCode}.";
        }

        return string.Join(" ", messages);
    }
}

public class FinixErrorDetail
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? LogRef { get; set; }
    public string? Transfer { get; set; }
}

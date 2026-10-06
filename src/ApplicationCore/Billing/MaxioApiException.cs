using System.Collections.Generic;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class MaxioApiException : System.Exception
{
    public MaxioApiException(
        string operation,
        HttpStatusCode statusCode,
        string? responseContent,
        IReadOnlyList<string>? errorMessages = null)
        : base(BuildMessage(operation, statusCode, responseContent, errorMessages))
    {
        Operation = operation;
        StatusCode = statusCode;
        ResponseContent = responseContent;
        ErrorMessages = errorMessages ?? System.Array.Empty<string>();
    }

    public string Operation { get; }
    public HttpStatusCode StatusCode { get; }
    public string? ResponseContent { get; }
    public IReadOnlyList<string> ErrorMessages { get; }
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsUnprocessableEntity => StatusCode == HttpStatusCode.UnprocessableEntity;

    private static string BuildMessage(
        string operation,
        HttpStatusCode statusCode,
        string? responseContent,
        IReadOnlyList<string>? errorMessages)
    {
        if (errorMessages is { Count: > 0 })
        {
            return $"Maxio {operation} failed with {(int)statusCode}: {string.Join("; ", errorMessages)}";
        }

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return $"Maxio {operation} failed with HTTP status {(int)statusCode}.";
        }

        return $"Maxio {operation} failed with HTTP status {(int)statusCode}: {responseContent}";
    }
}

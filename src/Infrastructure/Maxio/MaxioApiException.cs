using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio configuration is missing or invalid.
/// </summary>
public class MaxioConfigurationException : BillingProviderException
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response.
/// Error bodies per the OpenAPI spec are one of:
/// {"errors": ["..."]}, {"errors": {"field": "message"}} or {"error": "message"}.
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API returned {(System.Net.HttpStatusCode)statusCode}: {string.Join(" ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsNotFound => StatusCode == 404;

    public bool IsConflict => StatusCode == 422;
}
using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API answers with a non-success status.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API request failed with HTTP {statusCode}: {Summarize(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string ResponseBody { get; }

    private static string Summarize(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "(no response body)";
        }

        var trimmed = responseBody.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "...";
    }
}

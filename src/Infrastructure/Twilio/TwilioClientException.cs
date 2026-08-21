using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioClientException : Exception
{
    public TwilioClientException(int statusCode, int? providerCode, string operation)
        : base($"Twilio {operation} failed with HTTP {statusCode}" +
               (providerCode.HasValue ? $" (provider code {providerCode.Value})" : string.Empty) + ".")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
        Operation = operation;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
    public string Operation { get; }

    public bool IsClientError => StatusCode >= 400 && StatusCode < 500;
}

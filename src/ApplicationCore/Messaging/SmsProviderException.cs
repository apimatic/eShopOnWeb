using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public class SmsProviderException : Exception
{
    public SmsProviderException(string operation, int statusCode, int? providerErrorCode)
        : base($"{operation} failed with HTTP {statusCode}" + (providerErrorCode.HasValue ? $" (provider code {providerErrorCode})." : "."))
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int StatusCode { get; }
    public int? ProviderErrorCode { get; }
}

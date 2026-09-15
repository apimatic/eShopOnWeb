using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed record ValidatedPhoneNumber(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatusCode, int? providerErrorCode)
        : base("The messaging provider rejected the request.")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}

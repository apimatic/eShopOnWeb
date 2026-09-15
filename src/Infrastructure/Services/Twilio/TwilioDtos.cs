using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal sealed class TwilioMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

internal sealed class TwilioMessageListDto
{
    public List<TwilioMessageDto> Messages { get; set; } = new();
    public string? NextPageUri { get; set; }
}

internal sealed class TwilioLookupDto
{
    public bool Valid { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
    public List<string>? ValidationErrors { get; set; }
    public TwilioLineTypeIntelligenceDto? LineTypeIntelligence { get; set; }
}

internal sealed class TwilioLineTypeIntelligenceDto
{
    public string? Type { get; set; }
    public int? ErrorCode { get; set; }
}

internal sealed class TwilioErrorDto
{
    public int Code { get; set; }
    public string? Message { get; set; }
    public int Status { get; set; }
}

public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? providerCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ProviderCode { get; }
}

using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

/// <summary>
/// A Message resource as the provider returns it (schema <c>api.v2010.account.message</c>). Property
/// names map from the provider's snake_case JSON via a snake-case naming policy.
/// </summary>
public class TwilioMessageResource
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>RFC-2822 timestamp of when the message was sent (null for not-yet-sent messages).</summary>
    public string? DateSent { get; set; }

    public string? MessagingServiceSid { get; set; }
    public string? Direction { get; set; }
}

/// <summary>A page of the Message list resource (schema <c>ListMessageResponse</c>).</summary>
public class TwilioMessageListResponse
{
    public List<TwilioMessageResource> Messages { get; set; } = new();

    /// <summary>Relative URI of the next page, or null when there are no more.</summary>
    public string? NextPageUri { get; set; }
}

/// <summary>The provider's error model, returned on a non-2xx response.</summary>
public class TwilioErrorResponse
{
    public int? Code { get; set; }
    public string? Message { get; set; }
    public string? MoreInfo { get; set; }
    public int? Status { get; set; }
}

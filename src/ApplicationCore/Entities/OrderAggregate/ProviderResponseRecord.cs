using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// One raw response the payment provider returned for a payment attempt or a refund, kept verbatim
/// (including fields this build does not know about) for support staff.
/// </summary>
public class ProviderResponseRecord : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ProviderResponseRecord() {}

    public ProviderResponseRecord(DateTimeOffset receivedAt, int httpStatusCode, string body)
    {
        ReceivedAt = receivedAt;
        HttpStatusCode = httpStatusCode;
        Body = body;
    }

    public DateTimeOffset ReceivedAt { get; private set; }
    public int HttpStatusCode { get; private set; }
    public string Body { get; private set; }
}

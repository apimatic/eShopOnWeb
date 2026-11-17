using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest
{
    public ReconciliationRequest(DateTimeOffset? from, DateTimeOffset? to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }
}

using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciledMessage> Matched { get; set; } = new List<ReconciledMessage>();
    public List<ReconciledMessage> ProviderOnly { get; set; } = new List<ReconciledMessage>();
    public List<ReconciledMessage> EshopOnly { get; set; } = new List<ReconciledMessage>();
}

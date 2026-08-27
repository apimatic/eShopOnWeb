using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public ReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationItem> Items { get; set; } = new();
}

using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int ApplicationCount { get; set; }
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<ReconciliationProviderMessage> ProviderOnly { get; set; } = new();
    public List<ReconciliationApplicationMessage> ApplicationOnly { get; set; } = new();
}

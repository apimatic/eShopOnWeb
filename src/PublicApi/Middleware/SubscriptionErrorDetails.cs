using System;
using System.Collections.Generic;
using BlazorShared.Models;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

/// <summary>Error body for subscription billing failures: a caller-safe message plus the provider's reasons, when it gave any.</summary>
public class SubscriptionErrorDetails : ErrorDetails
{
    public IReadOnlyList<string> Details { get; set; } = Array.Empty<string>();
}

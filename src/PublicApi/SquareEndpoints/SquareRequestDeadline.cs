using System;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SquareEndpoints;

/// <summary>
/// The total time budget of a Square-backed request: every SDK call the handler makes runs under this
/// token, and a client that disconnects stops the outbound work too.
/// </summary>
public static class SquareRequestDeadline
{
    public static CancellationTokenSource Start(HttpContext httpContext, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        cts.CancelAfter(budget);
        return cts;
    }
}

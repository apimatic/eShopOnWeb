using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of bills raised in a date range and lines them up
/// against what eShop believes it raised, making plain which bills are eShop's and which are the provider
/// account's other activity. Restricted to the administrator role. <c>from</c> and <c>to</c> are ISO-8601
/// date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.ReconcileAsync(from, to, httpContext.RequestAborted))
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: reconciles the provider's own record of bills raised in a date range against what
/// eShop believes it raised, making plain which bills are eShop's and which were raised by other activity
/// on the shared provider account. <c>from</c> and <c>to</c> are ISO-8601 date-times. Restricted to the
/// administrator role.
/// </summary>
public class ReconciliationEndpoint : InvoiceEndpointBase, IEndpoint
{
    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoicingService invoicingService) =>
            {
                var report = await invoicingService.ReconcileAsync(from, to, RequestAborted);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("InvoiceEndpoints");
    }
}

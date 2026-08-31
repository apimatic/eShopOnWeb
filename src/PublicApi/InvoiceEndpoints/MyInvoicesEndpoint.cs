using System.Collections.Generic;
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
/// Lists the caller's own bills, each showing where it has got to and carrying its own <c>invoiceId</c>.
/// </summary>
public class MyInvoicesEndpoint : InvoiceEndpointBase, IEndpoint
{
    public MyInvoicesEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoicingService invoicingService) =>
            {
                var buyerId = CurrentUserName;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var invoices = await invoicingService.ListInvoicesForShopperAsync(buyerId, RequestAborted);
                return Results.Ok(invoices);
            })
            .Produces<IReadOnlyList<InvoiceSummaryView>>()
            .WithTags("InvoiceEndpoints");
    }
}

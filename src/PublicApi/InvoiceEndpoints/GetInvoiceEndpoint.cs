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
/// Reads one of the caller's bills: its current state, whatever the provider reports about how it reached
/// that state, and — once it has been put to the shopper — the top-level <c>paymentLink</c> to pay it.
/// A shopper can only ever read their own bills.
/// </summary>
public class GetInvoiceEndpoint : InvoiceEndpointBase, IEndpoint
{
    public GetInvoiceEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService invoicingService) =>
            {
                var buyerId = CurrentUserName;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var details = await invoicingService.GetInvoiceForShopperAsync(invoiceId, buyerId, RequestAborted);
                return Results.Ok(details);
            })
            .Produces<InvoiceDetails>()
            .WithTags("InvoiceEndpoints");
    }
}

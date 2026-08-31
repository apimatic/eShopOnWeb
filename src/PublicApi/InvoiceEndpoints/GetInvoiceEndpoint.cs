using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reads a bill's current state, the provider's account of how it reached it, and — once it has
/// been put to the shopper — how it can be paid. A shopper only ever sees their own bills.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                ClaimsPrincipal user,
                IInvoiceService invoiceService) =>
            {
                var view = await invoiceService.GetInvoiceAsync(invoiceId, user.GetBuyerId(), user.IsOperator());
                return Results.Ok(InvoiceResponse.From(view));
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

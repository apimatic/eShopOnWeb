using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Returns a bill's current state as the provider reports it, its history, and — once it has been put to
/// the shopper — a top-level payment link. A shopper may only read their own bill; an operator may read any.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.GetInvoiceAsync(invoiceId, httpContext.User, httpContext.RequestAborted))
            .Produces<GetInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

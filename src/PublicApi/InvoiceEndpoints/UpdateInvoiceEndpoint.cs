using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Corrects the due date or customer details on a bill that has not yet been put to the shopper. The
/// amount is not correctable — it comes from the order. Once the bill has been issued or withdrawn the
/// caller is told (409) rather than the change silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, AmendInvoiceRequest request, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.AmendInvoiceAsync(invoiceId, request, httpContext.User, httpContext.RequestAborted))
            .Produces<UpdateInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

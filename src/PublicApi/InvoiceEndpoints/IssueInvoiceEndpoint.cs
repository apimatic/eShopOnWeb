using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: puts a bill to the shopper (delivers it) so it becomes payable and a payment link is
/// available. Restricted to the administrator role.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.IssueInvoiceAsync(invoiceId, httpContext.RequestAborted))
            .Produces<IssueInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

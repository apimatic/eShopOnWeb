using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Returns the caller's own bills, each showing where it has got to. Each entry carries its own invoice id.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.GetMyInvoicesAsync(httpContext.User, httpContext.RequestAborted))
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and the
/// way to pay it is no longer handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.WithdrawInvoiceAsync(invoiceId, httpContext.RequestAborted))
            .Produces<WithdrawInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

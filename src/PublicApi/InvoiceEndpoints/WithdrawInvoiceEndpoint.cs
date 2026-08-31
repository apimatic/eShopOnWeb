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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and its
/// payment link is no longer handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : InvoiceEndpointBase, IEndpoint
{
    public WithdrawInvoiceEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService invoicingService) =>
            {
                var details = await invoicingService.WithdrawInvoiceAsync(invoiceId, RequestAborted);
                return Results.Ok(details);
            })
            .Produces<InvoiceDetails>()
            .WithTags("InvoiceEndpoints");
    }
}

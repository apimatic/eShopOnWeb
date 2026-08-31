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
/// Operator action: puts a bill to the shopper. Afterwards the bill reports itself as issued and a payment
/// link can be handed out. Restricted to the administrator role.
/// </summary>
public class IssueInvoiceEndpoint : InvoiceEndpointBase, IEndpoint
{
    public IssueInvoiceEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService invoicingService) =>
            {
                var details = await invoicingService.IssueInvoiceAsync(invoiceId, RequestAborted);
                return Results.Ok(details);
            })
            .Produces<InvoiceDetails>()
            .WithTags("InvoiceEndpoints");
    }
}

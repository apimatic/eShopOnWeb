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
/// Withdraws a bill that should not be paid (operator action). Afterwards it is no longer payable
/// and the way to pay it is no longer handed out.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                IInvoiceService invoiceService) =>
            {
                var view = await invoiceService.WithdrawInvoiceAsync(invoiceId);
                return Results.Ok(InvoiceResponse.From(view));
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

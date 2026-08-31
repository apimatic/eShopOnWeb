using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// the way to pay it is no longer handed out.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                var invoice = await invoiceService.WithdrawInvoiceAsync(invoiceId, cancellationToken);

                var response = new WithdrawInvoiceResponse
                {
                    InvoiceId = invoice.ProviderInvoiceId,
                    Status = invoice.ProviderStatus,
                    State = invoice.LifecycleState.ToString(),
                    Payable = invoice.LifecycleState == InvoiceLifecycleState.Issued
                };

                return Results.Ok(response);
            })
            .Produces<WithdrawInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

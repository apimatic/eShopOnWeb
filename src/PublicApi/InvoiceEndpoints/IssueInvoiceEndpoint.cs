using System.Threading;
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
/// Operator action: puts a bill to the shopper. Afterwards the application can hand out a way to pay
/// it, and the bill reports itself as having been put to the shopper.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                var detail = await invoiceService.IssueInvoiceAsync(invoiceId, cancellationToken);

                var response = new IssueInvoiceResponse
                {
                    InvoiceId = detail.Provider.Id,
                    Status = detail.Provider.Status,
                    State = detail.Local?.LifecycleState.ToString() ?? string.Empty,
                    PaymentLink = detail.Provider.PaymentLink
                };

                return Results.Ok(response);
            })
            .Produces<IssueInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

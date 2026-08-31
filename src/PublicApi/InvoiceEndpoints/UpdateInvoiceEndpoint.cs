using System.Security.Claims;
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
/// Corrects the due date or the customer details on the caller's bill, while it has not yet been put
/// to the shopper. Once the bill has been put to the shopper or withdrawn, the caller is told the
/// correction is no longer possible rather than it silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                CorrectInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                var invoice = await invoiceService.CorrectInvoiceAsync(
                    invoiceId,
                    user.BuyerId(),
                    request.DueDate,
                    request.CustomerName,
                    request.CustomerEmail,
                    cancellationToken);

                var response = new CorrectInvoiceResponse(request.CorrelationId())
                {
                    InvoiceId = invoice.ProviderInvoiceId,
                    OrderId = invoice.OrderId,
                    State = invoice.LifecycleState.ToString(),
                    DueDate = invoice.DueDate,
                    CustomerName = invoice.CustomerName,
                    CustomerEmail = invoice.CustomerEmail,
                    Amount = invoice.TotalAmount,
                    Currency = invoice.Currency
                };

                return Results.Ok(response);
            })
            .Produces<CorrectInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

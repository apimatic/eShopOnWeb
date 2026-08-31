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
/// Returns a bill's current state as the provider reports it, how it reached that state, and — once
/// it has been put to the shopper — how it can be paid. Shopper-scoped: a shopper only sees their own
/// bills; an operator can read any bill.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                var detail = await invoiceService.GetInvoiceAsync(invoiceId, user.BuyerId(), user.IsOperator(), cancellationToken);
                var provider = detail.Provider;
                var local = detail.Local;

                var response = new GetInvoiceResponse
                {
                    InvoiceId = provider.Id,
                    OrderId = local?.OrderId,
                    Status = provider.Status,
                    State = local?.LifecycleState.ToString(),
                    Amount = provider.TotalAmount ?? local?.TotalAmount,
                    Currency = provider.Currency ?? local?.Currency,
                    DueDate = provider.DueDate ?? local?.DueDate,
                    CustomerName = provider.CustomerName ?? local?.CustomerName,
                    CustomerEmail = provider.CustomerEmail ?? local?.CustomerEmail,
                    CreatedDate = provider.CreatedDate ?? local?.CreatedDate,
                    PaymentLink = provider.PaymentLink,
                    History = InvoiceMappings.ToHistory(provider)
                };

                return Results.Ok(response);
            })
            .Produces<GetInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}

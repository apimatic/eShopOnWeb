using System;
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
/// Raises a bill with the provider for one of the caller's orders. What is billed comes from the
/// order itself; the request only carries the due date. The bill starts out not yet put to the shopper.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RaiseInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                if (request.DueDate == default)
                {
                    return Results.BadRequest(new { message = "A due date is required." });
                }

                if (request.DueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
                {
                    return Results.BadRequest(new { message = "The due date cannot be in the past." });
                }

                var invoice = await invoiceService.RaiseInvoiceAsync(orderId, user.BuyerId(), request.DueDate, cancellationToken);

                var response = new RaiseInvoiceResponse(request.CorrelationId())
                {
                    InvoiceId = invoice.ProviderInvoiceId,
                    OrderId = invoice.OrderId,
                    State = invoice.LifecycleState.ToString(),
                    ProviderStatus = invoice.ProviderStatus,
                    Amount = invoice.TotalAmount,
                    Currency = invoice.Currency,
                    DueDate = invoice.DueDate,
                    CreatedDate = invoice.CreatedDate
                };

                return Results.Created($"api/invoices/{invoice.ProviderInvoiceId}", response);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }
}

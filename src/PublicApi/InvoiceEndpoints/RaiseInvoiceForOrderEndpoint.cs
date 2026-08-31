using System.Security.Claims;
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
/// Raises a bill with the provider for the caller's own order. What is billed comes from the order — its
/// items and what they cost. The bill starts out not yet put to the shopper.
/// </summary>
public class RaiseInvoiceForOrderEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, ClaimsPrincipal, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, invoiceService);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(RaiseInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.DueDate == default)
        {
            return Results.BadRequest("A due date is required (e.g. \"dueDate\": \"2026-09-30\").");
        }

        var invoice = await invoiceService.RaiseInvoiceForOrderAsync(
            request.OrderId, buyerId, request.DueDate, request.CustomerName, request.CustomerEmail);

        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = invoice.ProviderInvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            OrderId = invoice.OrderId,
            Status = invoice.Status.ToString(),
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate
        };

        return Results.Created($"api/invoices/{invoice.ProviderInvoiceId}", response);
    }
}

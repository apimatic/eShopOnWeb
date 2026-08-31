using System;
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
/// Raises a bill with the provider for one of the caller's own orders. The bill starts out as a
/// draft — not yet put to the shopper. Shopper-scoped: the order must belong to the caller.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, HttpContext http, IInvoiceService invoiceService) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, invoiceService);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(RaiseInvoiceRequest request, IInvoiceService invoiceService)
    {
        if (request.DueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return Results.BadRequest("The due date must be today or in the future.");
        }

        var result = await invoiceService.RaiseInvoiceAsync(request.OrderId, request.BuyerId, request.DueDate);

        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = result.InvoiceId,
            OrderId = result.OrderId,
            Status = result.Status,
            Amount = result.Amount,
            Currency = result.Currency,
            DueDate = result.DueDate
        };

        return Results.Created($"api/invoices/{result.InvoiceId}", response);
    }
}

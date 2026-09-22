using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, System.Threading.CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name;
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = request.Items
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, request.ShipToAddress?.ToAddress(), request.Cancellation);
        return Results.Created($"/api/orders/{orderId}", new { orderId, status = "AwaitingPayment" });
    }
}

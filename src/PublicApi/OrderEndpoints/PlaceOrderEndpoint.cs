using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PlaceOrderLine(int CatalogItemId, int Quantity);

public class PlaceOrderRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog item ids + quantities.
/// The order starts awaiting payment. Shopper-scoped.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPlacementService orderService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var lines = (request.Items ?? new List<PlaceOrderLine>())
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                var orderId = await orderService.PlaceOrderAsync(buyerId, lines, ct);
                return Results.Created($"api/orders/{orderId}",
                    new PlaceOrderResponse { OrderId = orderId, Status = "AwaitingPayment" });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}

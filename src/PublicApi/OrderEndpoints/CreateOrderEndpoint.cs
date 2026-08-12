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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items and quantities, reusing the app's existing
/// order/order-item model. The shopper is then told their order was placed (best-effort; a message that
/// cannot be sent never fails the order).
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    // API-placed orders collect no shipping address; the order model requires one, so a placeholder is used.
    private static Address DefaultShipToAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderService orderService,
                IReadRepository<CatalogItem> catalogRepository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request?.Items is null || request.Items.Count == 0)
                    return Results.BadRequest(new { message = "At least one order item is required." });

                if (request.Items.Any(i => i.Quantity < 1))
                    return Results.BadRequest(new { message = "Every item quantity must be at least 1." });

                var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await catalogRepository.ListAsync(new CatalogItemsSpecification(requestedIds), ct);
                if (catalogItems.Count != requestedIds.Length)
                    return Results.BadRequest(new { message = "One or more catalog items do not exist." });

                var inputs = request.Items
                    .Select(i => new OrderItemInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                var order = await orderService.CreateOrderAsync(buyerId, inputs, DefaultShipToAddress());

                await notifications.NotifyOrderPlacedAsync(order, ct);

                var response = new CreateOrderResponse(order.Id, order.Total(), order.Status.ToString());
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

public record CreateOrderItemDto(int CatalogItemId, int Quantity);

public record CreateOrderRequest(List<CreateOrderItemDto> Items);

/// <summary><c>orderId</c> is the identifier for driving the rest of the flow.</summary>
public record CreateOrderResponse(int OrderId, decimal Total, string Status);

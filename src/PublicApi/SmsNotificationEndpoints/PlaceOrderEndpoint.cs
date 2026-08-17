using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper (reusing eShop's own
/// order/order-item model) and tells them it was placed. Returns the new <c>orderId</c>.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                IOrderNotificationService orderNotificationService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "An order must contain at least one item." });
                }

                var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
                var shipToAddress = ToAddress(request.ShipToAddress);

                try
                {
                    var order = await orderNotificationService.PlaceOrderAsync(buyerId, lines, shipToAddress, cancellationToken);
                    var response = new PlaceOrderResponse
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total()
                    };
                    return Results.Created($"api/orders/{order.Id}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(ShipToAddressDto? dto)
    {
        if (dto is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            Coalesce(dto.Street),
            Coalesce(dto.City),
            Coalesce(dto.State),
            Coalesce(dto.Country),
            Coalesce(dto.ZipCode));
    }

    private static string Coalesce(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;
}

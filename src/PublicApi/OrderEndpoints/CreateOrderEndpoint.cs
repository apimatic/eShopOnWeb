using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order. The caller's identity comes from the token.</summary>
    public List<OrderLineRequest> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the order that was placed.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items, reusing the app's existing order/order-item
/// model. The shopper is told (by SMS) that their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, INotificationService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "At least one order item is required." });
                }

                var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

                try
                {
                    var order = await service.PlaceOrderAsync(buyerId, lines);
                    var response = new CreateOrderResponse(request.CorrelationId())
                    {
                        OrderId = order.Id,
                        Status = order.Status.ToString(),
                        Total = order.Total()
                    };
                    return Results.Created($"api/orders/{order.Id}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ICatalogOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ICatalogOrderService orders) =>
            {
                return await HandleAsync(request, orders);
            })
            .Produces<PlaceOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ICatalogOrderService orders)
    {
        var http = _httpContextAccessor.HttpContext;
        var buyerId = http?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cancellationToken = http?.RequestAborted ?? default;
        try
        {
            var lines = (request.Items ?? new List<PlaceOrderItemRequest>())
                .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                .ToList();
            var order = await orders.PlaceAsync(buyerId, lines, cancellationToken);
            await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);

            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

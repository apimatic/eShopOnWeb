using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids + quantities (reusing the app's
/// existing order/order-item model) and notifies the shopper that their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderNotificationService service) =>
            {
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, ct);
        if (result.Status == PlaceOrderStatus.InvalidRequest)
        {
            return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest,
                title: "The order could not be placed.");
        }

        // Include what was sent, so the flow can be driven end to end from the response.
        var notifications = await service.GetOrderNotificationsForOwnerAsync(result.OrderId, request.BuyerId, ct);
        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = result.OrderId };
        if (notifications is not null)
        {
            response.Notifications.AddRange(notifications.Select(NotificationDto.From));
        }
        return Results.Created($"api/orders/{result.OrderId}", response);
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto>? Items { get; set; }
    public string? BuyerId { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderStatusChangeResponse : BaseResponse
{
    public OrderStatusChangeResponse(Guid correlationId) : base(correlationId) { }
    public OrderStatusChangeResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders/{orderId}/dispatch — operator marks the order dispatched; the shopper is
/// told it is on its way and a delivery follow-up is queued with the provider for later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, service);
            })
            .Produces<OrderStatusChangeResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service)
    {
        var found = await service.DispatchAsync(request.OrderId, CancellationToken.None);
        if (!found)
            return Results.NotFound();

        return Results.Ok(new OrderStatusChangeResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Dispatched"
        });
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = "Cancelled";
}

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any not-yet-sent delivery follow-up
/// is called off so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service)
    {
        await service.CancelOrderAsync(request.OrderId);
        return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = request.OrderId });
    }
}

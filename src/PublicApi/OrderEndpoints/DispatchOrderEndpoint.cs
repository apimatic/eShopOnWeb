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

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a follow-up
/// asking how the delivery went is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ISmsNotificationService service) =>
                await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, service))
            .Produces<OrderTransitionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, ISmsNotificationService service)
    {
        var exists = await service.DispatchOrderAsync(request.OrderId);
        return exists
            ? Results.Ok(new OrderTransitionResponse(request.CorrelationId()) { OrderId = request.OrderId, Status = "Dispatched" })
            : Results.NotFound();
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderTransitionResponse : BaseResponse
{
    public OrderTransitionResponse(Guid correlationId) : base(correlationId) { }
    public OrderTransitionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

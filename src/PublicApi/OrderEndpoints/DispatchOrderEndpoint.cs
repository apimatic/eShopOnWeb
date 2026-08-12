using System;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks the order dispatched. The shopper is told it is on its way, and a
/// "how did delivery go?" follow-up is queued with the provider for a few days later.
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
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service)
    {
        var result = await service.DispatchOrderAsync(request.OrderId);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound();
        }
        if (result.Status == ResultStatus.Error)
        {
            return Results.Conflict(string.Join("; ", result.Errors));
        }
        return Results.Ok(new OrderActionResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Dispatched"
        });
    }
}

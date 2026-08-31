using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator: cancels an order. The shopper is told, and any delivery follow-up that has
/// not yet gone out is called off at the provider.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public CancelOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId));
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        try
        {
            var order = await _orderNotificationService.CancelAsync(request.OrderId);
            var response = new CancelOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Ok(response);
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

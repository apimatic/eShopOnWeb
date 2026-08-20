using System;
using System.Collections.Generic;
using System.Linq;
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

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderFulfillmentService fulfillmentService) =>
            {
                try
                {
                    var result = await fulfillmentService.CancelAsync(orderId, httpContext.RequestAborted);
                    var response = new CancelOrderResponse
                    {
                        OrderId = result.Order.Id,
                        Status = result.Order.Status.ToString(),
                        Notifications = result.Notifications.Select(NotificationMapper.ToDto).Where(d => d != null).Cast<NotificationDto>().ToList()
                    };
                    return Results.Ok(response);
                }
                catch (OrderNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOrderTransitionException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderFulfillmentService fulfillmentService)
        => Task.FromResult(Results.Ok());
}

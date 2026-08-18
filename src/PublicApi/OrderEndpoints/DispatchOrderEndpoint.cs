using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a delivery-feedback
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _service;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService service)
    {
        _orderRepository = orderRepository;
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId));
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        try
        {
            await _service.DispatchOrderAsync(order);
        }
        catch (OrderStatusException ex)
        {
            return Results.Conflict(ex.Message);
        }

        return Results.Ok(new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

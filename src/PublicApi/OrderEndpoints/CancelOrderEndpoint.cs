using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepository) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId));
                if (order is null)
                {
                    return Results.NotFound();
                }

                try
                {
                    order.MarkCancelled();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { errors = new[] { ex.Message } });
                }

                await orderRepository.UpdateAsync(order);
                await _notifications.NotifyOrderCancelledAsync(order);

                return Results.Ok(new OrderActionResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public System.Threading.Tasks.Task<IResult> HandleAsync(int request, IRepository<Order> orderRepository)
        => throw new NotSupportedException();
}

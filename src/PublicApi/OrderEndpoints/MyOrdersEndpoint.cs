using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    /// <summary>Populated from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new List<MyOrderDto>();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its
/// notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>>
{
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public MyOrdersEndpoint(
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity!.Name! }, orderRepository);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> orderRepository)
    {
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new OrderNotificationsSpecification(order.Id));

            // No callback URL exists, so delivery outcomes are pulled from the provider.
            await _notificationService.RefreshStatusesAsync(notifications);

            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
            });
        }

        return Results.Ok(response);
    }
}

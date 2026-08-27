using System;
using System.Collections.Generic;
using System.Linq;
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

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool CallerIsAdministrator { get; set; }
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) {}
    public ListOrderNotificationsResponse() {}

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Shows what was sent for an order and what became of each message. Shoppers can only
/// see their own orders; administrators can see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                var request = new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    CallerId = httpContext.User.Identity?.Name ?? string.Empty,
                    CallerIsAdministrator = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                };
                return await HandleAsync(request);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
        }

        if (!request.CallerIsAdministrator && order.BuyerId != request.CallerId)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });
        }

        await _notificationService.RefreshOrderNotificationStatusesAsync(order.Id);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.OrderBy(n => n.CreatedAt).Select(n => new OrderNotificationDto
            {
                NotificationId = n.Id,
                Type = n.Type.ToString(),
                Status = n.Status,
                ProviderMessageSid = n.ProviderMessageSid,
                Body = n.ContentRedacted ? null : n.Body,
                ContentRedacted = n.ContentRedacted,
                ErrorCode = n.ErrorCode,
                ErrorMessage = n.ErrorMessage,
                ScheduledFor = n.ScheduledFor,
                CreatedAt = n.CreatedAt,
                LastUpdatedAt = n.LastUpdatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

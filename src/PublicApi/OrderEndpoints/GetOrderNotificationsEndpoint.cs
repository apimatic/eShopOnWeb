using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public string? SubmitError { get; set; }
    public int? ResendOfNotificationId { get; set; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), service);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.GetRequiredUserName();
        var notifications = await service.ListForOrderAsync(request.OrderId, buyerId, http.IsAdministrator());
        var response = new GetOrderNotificationsResponse { OrderId = request.OrderId };
        response.Notifications.AddRange(notifications.Select(n => new OrderNotificationDto
        {
            NotificationId = n.NotificationId,
            OrderId = n.OrderId,
            Kind = n.Kind.ToString(),
            ProviderMessageSid = n.ProviderMessageSid,
            ProviderStatus = n.ProviderStatus,
            ProviderErrorCode = n.ProviderErrorCode,
            Body = n.Body,
            ContentRedacted = n.ContentRedacted,
            CreatedAt = n.CreatedAt,
            ScheduledFor = n.ScheduledFor,
            ProviderDateSent = n.ProviderDateSent,
            SubmitError = n.SubmitError,
            ResendOfNotificationId = n.ResendOfNotificationId
        }));
        return Results.Ok(response);
    }
}

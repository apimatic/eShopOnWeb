using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationPathRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    public int NotificationId { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationPathRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(
                    new ResendNotificationPathRequest { NotificationId = notificationId, IdempotencyKey = request.IdempotencyKey },
                    orders,
                    http);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationPathRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(ResendNotificationPathRequest request, IShopperOrderService orders, HttpContext http)
    {
        var notificationId = await orders.ResendAsync(request.NotificationId, request.IdempotencyKey, http.RequestAborted);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notificationId
        });
    }
}

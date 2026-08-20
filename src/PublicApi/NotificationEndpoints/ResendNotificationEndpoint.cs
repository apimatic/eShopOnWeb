using System.Threading.Tasks;
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

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, HttpContext httpContext, IShopOrderService orderService) =>
            {
                return await HandleAsync(request, orderService, notificationId, httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopOrderService orderService)
        => HandleAsync(request, orderService, 0, null!);

    private async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        IShopOrderService orderService,
        int notificationId,
        HttpContext httpContext)
    {
        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) && httpContext != null)
        {
            key = httpContext.Request.Headers["Idempotency-Key"].ToString();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var notification = await orderService.ResendAsync(notificationId, key.Trim());
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}

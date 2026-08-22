using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationService orderNotificationService) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest(notificationId, request), orderNotificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationService orderNotificationService)
    {
        var result = await orderNotificationService.ResendAsync(request.NotificationId, request.Body.IdempotencyKey);
        return result.ToHttpResult(notification => Results.Ok(new ResendNotificationResponse(request.Body.CorrelationId())
        {
            NotificationId = notification.Id,
            OriginalNotificationId = request.NotificationId,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus
        }));
    }
}

public class ResendNotificationRouteRequest
{
    public ResendNotificationRouteRequest(int notificationId, ResendNotificationRequest body)
    {
        NotificationId = notificationId;
        Body = body;
    }

    public int NotificationId { get; }
    public ResendNotificationRequest Body { get; }
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
    public int OriginalNotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
}

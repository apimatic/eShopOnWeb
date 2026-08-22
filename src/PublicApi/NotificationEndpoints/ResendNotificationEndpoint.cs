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
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, INotificationOperatorService notifications) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, notifications);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperatorService notifications)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var result = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (result == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.NotificationId
        });
    }
}

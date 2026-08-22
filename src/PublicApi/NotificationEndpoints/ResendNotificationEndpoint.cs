using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationWorkflow>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationWorkflow workflow) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = request.IdempotencyKey
                }, workflow);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationWorkflow workflow)
    {
        var result = await workflow.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (!result.Succeeded || result.Notification == null)
        {
            return ApiResults.From(result.StatusCode, error: result.Error);
        }

        return ApiResults.From(result.StatusCode, new ResendNotificationResponse
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.Status,
            ProviderMessageSid = result.Notification.ProviderMessageSid
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRouteRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

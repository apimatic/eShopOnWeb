using System.Collections.Generic;
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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, IOrderNotificationService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        try
        {
            var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey ?? string.Empty);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                OriginalNotificationId = request.NotificationId,
                DeliveryStatus = notification.DeliveryStatus,
                ProviderMessageSid = notification.ProviderMessageSid
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string? IdempotencyKey { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

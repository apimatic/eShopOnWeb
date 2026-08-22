using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorNotificationService operatorNotificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, operatorNotificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorNotificationService operatorNotificationService)
    {
        var notification = await operatorNotificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            OriginalNotificationId = request.NotificationId,
            DeliveryStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        };

        return Results.Ok(response);
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

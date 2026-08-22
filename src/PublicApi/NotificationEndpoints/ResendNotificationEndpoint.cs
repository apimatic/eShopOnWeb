using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, IOrderNotificationService notifications) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(request, notifications);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notifications)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var created = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = created.Id,
                OriginalNotificationId = created.OriginalNotificationId,
                ProviderMessageSid = created.ProviderMessageSid,
                Status = created.ProviderStatus
            });
        }
        catch (NotificationActionException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }
        catch (NotificationActionException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    internal int NotificationId { get; set; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int? OriginalNotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
}

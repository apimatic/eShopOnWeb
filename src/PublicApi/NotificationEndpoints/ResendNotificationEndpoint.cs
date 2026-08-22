using System.Threading.Tasks;
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
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, IOrderNotificationService notifications) =>
            {
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) &&
                    httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
                {
                    idempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = idempotencyKey ?? string.Empty
                }, notifications);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notifications)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var resent = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resent.Id,
            SourceNotificationId = resent.SourceNotificationId,
            ProviderStatus = resent.ProviderStatus,
            ProviderMessageSid = resent.ProviderMessageSid
        });
    }
}

public class ResendNotificationRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int? SourceNotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageSid { get; set; }
}

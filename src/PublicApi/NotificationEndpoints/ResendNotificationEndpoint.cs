using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, INotificationOperatorService notifications, HttpContext http) =>
            {
                var request = new ResendNotificationRequest { NotificationId = notificationId };

                if (http.Request.ContentLength > 0
                    || string.Equals(http.Request.ContentType, "application/json", System.StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var body = await http.Request.ReadFromJsonAsync<ResendNotificationRequest>();
                        if (body is not null)
                        {
                            request.IdempotencyKey = body.IdempotencyKey;
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        return Results.BadRequest(new { message = "Request body must be JSON with idempotencyKey." });
                    }
                }

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

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
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        try
        {
            var resent = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = resent.Id,
                OriginalNotificationId = request.NotificationId,
                ProviderMessageSid = resent.ProviderMessageSid,
                ProviderStatus = resent.ProviderStatus
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
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
    public int OriginalNotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
}

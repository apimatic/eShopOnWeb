using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key; repeating it must not send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The notification the resend produced (top-level id operator endpoints act on).</summary>
    public int NotificationId { get; set; }
    public bool AlreadyProcessed { get; set; }
}

/// <summary>Operator action: re-send a notification that did not reach the shopper, idempotent on the key.</summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http,
             IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                // The key may come in the body or, conventionally, an Idempotency-Key header.
                var key = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                    key = header.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest("An idempotency key is required.");

                try
                {
                    var outcome = await service.ResendAsync(notificationId, key!, ct);
                    return Results.Ok(new ResendNotificationResponse
                    {
                        NotificationId = outcome.NotificationId,
                        AlreadyProcessed = outcome.AlreadyProcessed
                    });
                }
                catch (NotificationNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (NotificationValidationException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}

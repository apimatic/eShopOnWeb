using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach the
/// shopper. The caller-supplied idempotency key (the <c>Idempotency-Key</c> header) makes a repeat under the
/// same key a no-op; a fresh key is a legitimate new attempt. Returns the id of the message resend produced.
/// </summary>
public class ResendNotificationEndpoint
    : IEndpoint<IResult, int, IOrderNotificationService, HttpContext>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(notificationId, service, http))
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service, HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            return Results.BadRequest(new { error = $"The {IdempotencyKeyHeader} header is required." });
        }

        var result = await service.ResendAsync(notificationId, keyValues.ToString(), http.RequestAborted);
        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.NotificationId,
            Deduplicated = result.Deduplicated
        });
    }
}

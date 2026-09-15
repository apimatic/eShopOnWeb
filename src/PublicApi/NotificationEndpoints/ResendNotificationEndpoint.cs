using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message this resend produced (or replayed).</summary>
    public int NotificationId { get; set; }

    /// <summary>"sent" for a fresh key, "replayed" when the key had already been used.</summary>
    public string Outcome { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key in the <c>Idempotency-Key</c> header; repeating a request under the
/// same key returns the earlier result without sending again, while a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, INotificationAdminService>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, INotificationAdminService service) =>
            {
                var idempotencyKey = http.Request.Headers[IdempotencyKeyHeader].ToString();
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest(new { error = $"An '{IdempotencyKeyHeader}' header is required." });

                var result = await service.ResendAsync(notificationId, idempotencyKey);
                return result.Status switch
                {
                    ResendStatus.SourceNotFound => Results.NotFound(),
                    ResendStatus.ContentUnavailable => Results.Conflict(
                        new { error = "The message content has been disposed of and cannot be re-sent." }),
                    ResendStatus.ReplayedExisting => Results.Ok(
                        new ResendNotificationResponse { NotificationId = result.NotificationId!.Value, Outcome = "replayed" }),
                    _ => Results.Ok(
                        new ResendNotificationResponse { NotificationId = result.NotificationId!.Value, Outcome = "sent" })
                };
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(int notificationId, INotificationAdminService service) =>
        Task.FromResult(Results.Ok());
}

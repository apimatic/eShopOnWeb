using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>Identifier of the message the resend produced — a top-level field.</summary>
    public int NotificationId { get; set; }
    public string SendState { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Requires a caller-supplied idempotency
/// key (the <c>Idempotency-Key</c> header): repeating a request under the same key does not send a second
/// message and returns the notification already produced for it, while a fresh key sends again. Restricted to
/// the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http) => await HandleAsync(notificationId, http))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            return Results.BadRequest(new { message = $"An {IdempotencyKeyHeader} header is required." });
        }
        var idempotencyKey = keyValues.ToString();

        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();
        try
        {
            var notification = await notifier.ResendAsync(notificationId, idempotencyKey, http.RequestAborted);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                SendState = notification.SendState.ToString(),
                ProviderStatus = notification.ProviderStatus,
                ProviderMessageSid = notification.ProviderMessageSid
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

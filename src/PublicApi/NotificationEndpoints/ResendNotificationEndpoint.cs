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

public class ResendNotificationRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key returns the same result
    /// instead of sending again; a genuine second attempt uses a fresh key. May also be supplied via
    /// the <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Restricted to the administrator
/// role. Idempotent on the caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ResendNotificationRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, HttpContext http)
    {
        var notificationId = int.Parse((string)http.Request.RouteValues["notificationId"]!);

        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
            idempotencyKey = header.ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { error = "An idempotency key is required (request body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var service = http.RequestServices.GetRequiredService<IOrderNotificationService>();
        var resend = await service.ResendAsync(notificationId, idempotencyKey!, http.RequestAborted);
        if (resend is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            MessageSid = resend.MessageSid
        });
    }
}

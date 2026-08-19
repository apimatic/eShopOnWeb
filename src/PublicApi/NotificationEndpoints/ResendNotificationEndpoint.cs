using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key. May also be supplied via the <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not
/// reach the shopper. Idempotent on the caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, INotificationAdminService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;

                // A header value, if present, takes precedence over the body field.
                if (httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
                    request.IdempotencyKey = headerKey.ToString();

                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationAdminService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required (Idempotency-Key header or idempotencyKey field)." });

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!, CancellationToken.None);
        if (!result.Found)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.NotificationId,
            Note = result.Note
        });
    }
}

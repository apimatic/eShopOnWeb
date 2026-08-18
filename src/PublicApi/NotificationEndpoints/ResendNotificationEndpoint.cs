using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse
{
    public ResendNotificationResponse(int notificationId, string? status)
    {
        NotificationId = notificationId;
        Status = status;
    }

    /// <summary>The identifier of the message the re-send produced (a top-level field).</summary>
    public int NotificationId { get; set; }

    public string? Status { get; set; }
}

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. The caller supplies an idempotency
/// key (the <c>Idempotency-Key</c> header): repeating a request under the same key does not send a second
/// message, while a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, string, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, IOrderNotificationService notificationService) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = "An 'Idempotency-Key' header is required." });
                }

                return await HandleAsync(notificationId, idempotencyKey, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, string idempotencyKey, IOrderNotificationService notificationService)
    {
        var ct = _httpContextAccessor.RequestAborted();
        var result = await notificationService.ResendAsync(notificationId, idempotencyKey, ct);

        return result.Status switch
        {
            ResendStatus.SourceNotFound => Results.NotFound(),
            ResendStatus.NumberNoLongerOnFile => Results.Conflict(new { message = "The destination number is no longer on file; nothing was sent." }),
            _ => Results.Ok(new ResendNotificationResponse(result.Notification!.Id, result.Notification.Status))
        };
    }
}

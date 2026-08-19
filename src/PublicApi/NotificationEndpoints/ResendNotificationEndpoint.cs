using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. A caller-supplied
/// idempotency key makes a repeat request send no second message; a fresh key is a genuine retry.
/// Returns the identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, IOrderNotificationService service) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        var notificationId = request.NotificationId;

        // The idempotency key may come in the body or as an Idempotency-Key header.
        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key)
            && _httpContextAccessor.HttpContext!.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
        {
            key = headerValue.ToString();
        }

        var result = await service.ResendAsync(notificationId, key ?? string.Empty);
        return result.Outcome switch
        {
            ActionOutcome.BadRequest => Results.BadRequest(new { error = result.Error }),
            ActionOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            ActionOutcome.Conflict => Results.Conflict(new { error = result.Error, notificationId = result.NotificationId, status = result.Status }),
            _ => Results.Ok(new ResendNotificationResponse { NotificationId = result.NotificationId, Status = result.Status })
        };
    }
}

public class ResendNotificationRequest
{
    /// <summary>The message to re-send. Populated from the route by the endpoint.</summary>
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeats under the same key send no second message.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
}

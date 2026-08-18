using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this request repeated an earlier key and no new message was sent.</summary>
    public bool Deduplicated { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request
/// under the same idempotency key returns the message the first attempt produced without sending
/// again; a fresh key sends anew.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly IOrderNotificationService _orderNotificationService;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http) => await HandleAsync(notificationId, request, http))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http)
    {
        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
        {
            idempotencyKey = headerValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
        }

        var result = await _orderNotificationService.ResendAsync(notificationId, idempotencyKey!);
        if (!result.Found)
        {
            return result.Error is not null && result.Error.Contains("not found")
                ? Results.NotFound()
                : Results.BadRequest(new { error = result.Error });
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.NotificationId,
            Deduplicated = result.Deduplicated
        });
    }
}

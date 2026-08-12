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
    /// <summary>Caller-supplied idempotency key. May also be supplied via the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when a prior request under the same key already produced this message (no second send).</summary>
    public bool AlreadySent { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under
/// the same idempotency key does not send a second message; a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private readonly IOrderNotificationService _orderNotificationService;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http, CancellationToken ct) =>
                await HandleAsync(notificationId, request, http, ct))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http, CancellationToken ct)
    {
        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue(IdempotencyHeader, out var header))
            idempotencyKey = header.ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var result = await _orderNotificationService.ResendAsync(notificationId, idempotencyKey, ct);
        if (result is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.NotificationId,
            AlreadySent = result.WasAlreadySent
        });
    }
}

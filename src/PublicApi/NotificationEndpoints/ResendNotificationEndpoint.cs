using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>Top-level identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an idempotency
/// key via the <c>Idempotency-Key</c> header; repeating a request under the same key does not send a second
/// message, while a genuine second attempt under a fresh key does.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
                await HandleAsync(notificationId, service))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
    {
        var idempotencyKey = _httpContextAccessor.Header(IdempotencyKeyHeader);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { errors = new[] { $"An idempotency key is required (via the '{IdempotencyKeyHeader}' header)." } });
        }

        var result = await service.ResendAsync(notificationId, idempotencyKey, _httpContextAccessor.RequestAborted());
        if (!result.IsSuccess)
        {
            return result.ToStatusResult();
        }

        return Results.Ok(new ResendNotificationResponse { NotificationId = result.Value.Id });
    }
}

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

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(System.Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach
/// the shopper. Repeating under the same idempotency key does not send a second message; a fresh key
/// is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http) => await HandleAsync(notificationId, request, http))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http)
    {
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            http.Request.Headers.TryGetValue(IdempotencyHeader, out var headerValue))
        {
            idempotencyKey = headerValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var service = http.RequestServices.GetRequiredService<ISmsNotificationService>();
        var notification = await service.ResendAsync(notificationId, idempotencyKey!, http.RequestAborted);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            DeliveryStatus = notification.DeliveryStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        };
        return Results.Ok(response);
    }
}

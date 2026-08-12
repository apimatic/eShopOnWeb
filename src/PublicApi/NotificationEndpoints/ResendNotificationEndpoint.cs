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

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. The caller-supplied idempotency
/// key makes a repeat under the same key a no-op (the first result is returned), while a fresh key is a
/// legitimate second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationId, request ?? new ResendNotificationRequest(), httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;

        // The idempotency key may arrive in the body or the Idempotency-Key header.
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            httpContext.Request.Headers.TryGetValue(IdempotencyHeader, out var headerValue))
        {
            idempotencyKey = headerValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var result = await notificationService.ResendAsync(notificationId, idempotencyKey.Trim(), cancellationToken);
        if (result is null)
            return Results.NotFound();

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Id,
            SourceNotificationId = notificationId,
            DeliveryStatus = result.DeliveryStatus,
            ProviderMessageSid = result.ProviderMessageSid
        };
        return Results.Ok(response);
    }
}

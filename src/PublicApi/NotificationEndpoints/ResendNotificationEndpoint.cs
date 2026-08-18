using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key (the <c>Idempotency-Key</c> header or an
/// <c>idempotencyKey</c> body field); repeating a request under the same key does not send a
/// second message, while a fresh key does. Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
             ResendNotificationRequest? request,
             IOrderNotificationService notificationService) =>
                await HandleAsync(notificationId, idempotencyKeyHeader, request, notificationService))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        int notificationId,
        string? idempotencyKeyHeader,
        ResendNotificationRequest? request,
        IOrderNotificationService notificationService)
    {
        var idempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader)
            ? idempotencyKeyHeader
            : request?.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem("An idempotency key is required (Idempotency-Key header or idempotencyKey body field).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var resend = await notificationService.ResendAsync(notificationId, idempotencyKey);
            if (resend is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ResendNotificationResponse(resend.Id, resend.ProviderStatus));
        }
        catch (NotificationContentDisposedException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

public record ResendNotificationRequest(string? IdempotencyKey);

/// <summary>Carries the identifier of the message the resend produced as a top-level field.</summary>
public record ResendNotificationResponse(int NotificationId, string DeliveryStatus);

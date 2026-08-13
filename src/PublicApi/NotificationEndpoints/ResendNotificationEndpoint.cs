using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating a request under the same
/// idempotency key does not send a second message; a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResendNotificationRequest? body,
             HttpRequest httpRequest, INotificationOperationsService service) =>
            {
                // The body is optional: the idempotency key may instead arrive in the Idempotency-Key header.
                var request = body ?? new ResendNotificationRequest();
                request.NotificationId = notificationId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperationsService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required (request body 'idempotencyKey' or 'Idempotency-Key' header).");
        }

        var outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        if (!outcome.SourceFound || outcome.Result is null)
        {
            return Results.NotFound();
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = outcome.Result.Id,
            Replayed = outcome.Replayed,
            Status = outcome.Result.Status
        };
        return Results.Ok(response);
    }
}

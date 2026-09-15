using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach the
/// shopper. Idempotent on the caller-supplied key: the same key never sends twice; a fresh key is a
/// genuine new attempt. Administrator only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request,
             [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
             INotificationOperationsService service, CancellationToken ct) =>
            {
                var key = !string.IsNullOrWhiteSpace(idempotencyKeyHeader)
                    ? idempotencyKeyHeader!
                    : request?.IdempotencyKey;

                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var result = await service.ResendAsync(notificationId, key!, ct);
                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.NotificationId,
                    Replayed = result.Replayed
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    // The interface's HandleAsync is not used: the route handler above carries the header/body binding.
    public Task<IResult> HandleAsync(int notificationId, INotificationOperationsService service) =>
        Task.FromResult(Results.BadRequest());
}

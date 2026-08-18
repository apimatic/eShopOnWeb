using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key (request body or Idempotency-Key header) makes a repeat a no-op while a fresh key
/// is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request,
             [FromHeader(Name = "Idempotency-Key")] string? headerKey,
             IRepository<OrderNotification> repository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, request, headerKey, repository, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest? request,
        string? headerKey,
        IRepository<OrderNotification> repository,
        IOrderNotificationService notificationService)
    {
        var idempotencyKey = !string.IsNullOrWhiteSpace(request?.IdempotencyKey) ? request!.IdempotencyKey : headerKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem("An idempotency key is required (request body or Idempotency-Key header).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var original = await repository.GetByIdAsync(notificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        ResendResult result;
        try
        {
            result = await notificationService.ResendAsync(original, idempotencyKey);
        }
        catch (SmsGatewayException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        var produced = result.Notification;
        var response = new ResendNotificationResponse(request?.CorrelationId() ?? Guid.NewGuid())
        {
            NotificationId = produced.Id,
            ProviderMessageSid = produced.ProviderMessageSid,
            ProviderStatus = produced.ProviderStatus,
            Outcome = produced.Outcome.ToString(),
            Deduplicated = result.Deduplicated
        };
        return Results.Ok(response);
    }
}

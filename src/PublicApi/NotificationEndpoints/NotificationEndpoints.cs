using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

// ---------------------------------------------------------------------------------------------------
// Flow 3 — what the operator can do about it. Resend, content disposal and reconciliation are all
// operator (administrator) actions.
// ---------------------------------------------------------------------------------------------------

/// <summary>
/// POST api/notifications/{notificationId}/resend — operator re-sends a message that did not reach the
/// shopper. The caller-supplied idempotency key (Idempotency-Key header) makes a repeat a no-op.
/// Returns the identifier of the notification the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IRepository<OrderNotification>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
             IRepository<OrderNotification> repository, IOrderNotificationService notificationService) =>
                await HandleAsync(new ResendNotificationRequest(notificationId, idempotencyKey), repository, notificationService))
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request,
        IRepository<OrderNotification> repository, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An Idempotency-Key header is required." });
        }

        var original = await repository.GetByIdAsync(request.NotificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        try
        {
            var newNotificationId = await notificationService.ResendAsync(original, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(newNotificationId));
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the content was disposed of and can no longer be re-sent.
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>
/// DELETE api/notifications/{notificationId}/content — dispose of a message's content at the provider and
/// locally. The record that a message was sent, and what became of it, survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IRepository<OrderNotification>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> repository, IOrderNotificationService notificationService) =>
                await HandleAsync(notificationId, repository, notificationService))
            .Produces<DisposeContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId,
        IRepository<OrderNotification> repository, IOrderNotificationService notificationService)
    {
        var notification = await repository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        try
        {
            await notificationService.DisposeContentAsync(notification);
            return Results.Ok(new DisposeContentResponse(notification.Id, notification.ContentRedacted));
        }
        catch (SmsProviderException ex)
        {
            // The content could not be disposed of at the provider; do not claim it was.
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

/// <summary>
/// GET api/notifications/reconciliation?from=&amp;to= — line up the provider's own record of messages
/// sent from this application's configured number against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
                await HandleAsync(new ReconciliationRequest(from, to), notificationService))
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await notificationService.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}

// ----- DTOs -------------------------------------------------------------------------------------------

public record ResendNotificationRequest(int NotificationId, string? IdempotencyKey);

public record ResendNotificationResponse(int NotificationId);

public record DisposeContentResponse(int NotificationId, bool ContentRedacted);

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

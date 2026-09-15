using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _notifications;

    public ResendNotificationEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, CancellationToken cancellationToken) =>
                await HandleAsync(notificationId, request, cancellationToken))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            return Results.BadRequest(new { error = "idempotencyKey is required and must be at most 128 characters." });
        }

        var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            ResendNotificationOutcome.Created => Results.Created(
                $"/api/notifications/{result.Notification!.Id}",
                new { notificationId = result.Notification.Id }),
            ResendNotificationOutcome.Existing => Results.Ok(new { notificationId = result.Notification!.Id }),
            ResendNotificationOutcome.NotFound => Results.NotFound(),
            ResendNotificationOutcome.ContactRemoved => Results.Conflict(new { error = "The destination has been removed." }),
            ResendNotificationOutcome.ContentDisposed => Results.Conflict(new { error = "The message content has been disposed of." }),
            _ => Results.Conflict(new { error = "Only a message that did not reach the shopper can be resent." })
        };
    }

    public Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request) =>
        HandleAsync(notificationId, request, CancellationToken.None);
}

public sealed class DisposeNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderNotificationService _notifications;

    public DisposeNotificationContentEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken cancellationToken) => await HandleAsync(notificationId, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var result = await _notifications.DisposeContentAsync(notificationId, cancellationToken);
        return result.Outcome switch
        {
            ContentDisposalOutcome.Disposed or ContentDisposalOutcome.AlreadyDisposed => Results.NoContent(),
            ContentDisposalOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(
                new { error = "The provider could not confirm content disposal." },
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    public Task<IResult> HandleAsync(int notificationId) => HandleAsync(notificationId, CancellationToken.None);
}

public sealed class ReconcileNotificationsEndpoint : IEndpoint<IResult>
{
    private readonly IOrderNotificationService _notifications;

    public ReconcileNotificationsEndpoint(IOrderNotificationService notifications) => _notifications = notifications;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, CancellationToken cancellationToken) =>
                await HandleAsync(from, to, cancellationToken))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        string? from,
        string? to,
        CancellationToken cancellationToken = default)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedFrom) ||
            !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTo) ||
            parsedFrom >= parsedTo)
        {
            return Results.BadRequest(new { error = "from and to must be valid ISO-8601 date-times with from before to." });
        }

        try
        {
            var result = await _notifications.ReconcileAsync(parsedFrom, parsedTo, cancellationToken);
            return Results.Ok(new
            {
                from = result.From,
                to = result.To,
                interval = "open",
                entries = result.Entries
            });
        }
        catch (SmsProviderException)
        {
            return Results.Json(
                new { error = "The provider reconciliation request failed." },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    public Task<IResult> HandleAsync() => HandleAsync(null, null, CancellationToken.None);
}

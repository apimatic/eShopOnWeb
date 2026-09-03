using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    ResendNotificationRequest request,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 256)
                    {
                        return EndpointResults.BadRequest("An idempotency key of at most 256 characters is required.");
                    }

                    try
                    {
                        var resendId = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
                        return resendId is null
                            ? Results.NotFound()
                            : Results.Ok(new ResendNotificationResponse(resendId.Value));
                    }
                    catch (InvalidResendRequestException ex)
                    {
                        return EndpointResults.Conflict(ex.Message);
                    }
                    catch (TwilioProviderException)
                    {
                        return EndpointResults.ProviderUnavailable();
                    }
                })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("NotificationOperatorEndpoints");
    }
}

public sealed class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int notificationId,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var result = await service.DisposeNotificationContentAsync(notificationId, cancellationToken);
                        return result == ContentDisposalResult.NotFound ? Results.NotFound() : Results.NoContent();
                    }
                    catch (TwilioProviderException)
                    {
                        return EndpointResults.ProviderUnavailable();
                    }
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("NotificationOperatorEndpoints");
    }
}

public sealed class ReconcileNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    string? from,
                    string? to,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryParseIso8601(from, out var fromValue) ||
                        !EndpointResults.TryParseIso8601(to, out var toValue))
                    {
                        return EndpointResults.BadRequest("Both 'from' and 'to' must be ISO-8601 date-times with a UTC offset.");
                    }

                    try
                    {
                        var items = await service.ReconcileAsync(fromValue, toValue, cancellationToken);
                        return Results.Ok(new ReconciliationResponse(
                            fromValue,
                            toValue,
                            items.Select(OrderNotificationDtoMapper.ToDto).ToList()));
                    }
                    catch (InvalidReconciliationRangeException ex)
                    {
                        return EndpointResults.BadRequest(ex.Message);
                    }
                    catch (TwilioProviderException)
                    {
                        return EndpointResults.ProviderUnavailable();
                    }
                })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("NotificationOperatorEndpoints");
    }
}

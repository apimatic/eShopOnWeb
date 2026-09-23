using System;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using BlazorSharedAuth = BlazorShared.Authorization.Constants;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

// ----- DTOs -----

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key: repeating a request under the same key does not send twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int NotificationId { get; set; }
}

public record ResendNotificationResponse(int NotificationId, string Outcome, string? Message);

// ----- POST /api/notifications/{notificationId}/resend (operator) -----

public class NotificationResendEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorSharedAuth.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, INotificationAdminService service,
             CancellationToken ct) =>
            {
                request.NotificationId = notificationId;
                return await Execute(request, service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationAdminService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(ResendNotificationRequest request,
        INotificationAdminService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, ct);
        return result.Outcome switch
        {
            ResendOutcome.Sent or ResendOutcome.DuplicateIgnored => Results.Ok(
                new ResendNotificationResponse(result.NotificationId!.Value, result.Outcome.ToString(), result.Message)),
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.NothingToResend => Results.Conflict(new { message = result.Message }),
            _ => Results.Json(
                new ResendNotificationResponse(result.NotificationId ?? 0, result.Outcome.ToString(), result.Message),
                statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

// ----- DELETE /api/notifications/{notificationId}/content (operator) -----

public class NotificationContentDisposalEndpoint
    : IEndpoint<IResult, NotificationContentRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorSharedAuth.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service, CancellationToken ct) =>
                await Execute(new NotificationContentRequest { NotificationId = notificationId }, service, ct))
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(NotificationContentRequest request, INotificationAdminService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(NotificationContentRequest request,
        INotificationAdminService service, CancellationToken ct)
    {
        var result = await service.DisposeContentAsync(request.NotificationId, ct);
        return result.Outcome switch
        {
            ContentDisposalOutcome.Disposed or ContentDisposalOutcome.AlreadyDisposed
                or ContentDisposalOutcome.NothingToDispose => Results.NoContent(),
            ContentDisposalOutcome.NotFound => Results.NotFound(),
            _ => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

public class NotificationContentRequest
{
    public int NotificationId { get; set; }
}

// ----- GET /api/notifications/reconciliation?from=&to= (operator) -----

public class NotificationReconciliationEndpoint
    : IEndpoint<IResult, ReconciliationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorSharedAuth.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, INotificationAdminService service, CancellationToken ct) =>
                await Execute(new ReconciliationRequest { From = from, To = to }, service, ct))
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, INotificationAdminService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(ReconciliationRequest request,
        INotificationAdminService service, CancellationToken ct)
    {
        if (!TryParseIso(request.From, out var from) || !TryParseIso(request.To, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }
        if (from > to)
        {
            return Results.BadRequest(new { message = "from must not be after to." });
        }

        try
        {
            var report = await service.ReconcileAsync(from, to, ct);
            return Results.Ok(report);
        }
        catch (SmsProviderException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out result);
    }
}

public class ReconciliationRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

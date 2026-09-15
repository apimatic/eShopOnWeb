using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider's record does not contain.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; set; } = Array.Empty<ReconciliationEntry>();
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — the provider's own record of messages
/// for a date range lined up against what eShop believes it sent. Counts only messages sent from the
/// application's own configured sending number. Operator-only. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : ApiEndpointBase,
    IEndpoint<IResult, ReconciliationRequest, INotificationService>
{
    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationService notificationService) =>
                await HandleAsync(new ReconciliationRequest(from, to), notificationService))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationService notificationService)
    {
        if (request.To < request.From)
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

        var report = await notificationService.ReconcileAsync(request.From, request.To, Aborted);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            EShopOnly = report.EShopOnly
        };
        return Results.Ok(response);
    }
}

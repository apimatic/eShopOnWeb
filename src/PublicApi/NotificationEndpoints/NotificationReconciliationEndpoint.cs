using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lists the provider's own record of messages sent from this
/// application's configured sending number over a date range, lined up against what
/// eShop believes it sent. Covers the whole range.
/// </summary>
public class NotificationReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset?, DateTimeOffset?, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notificationService;

    public NotificationReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user) =>
            {
                return await HandleAsync(from, to, user);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user)
    {
        if (from == null || to == null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' (ISO-8601 date-times) are required." });
        }
        if (from >= to)
        {
            return Results.BadRequest(new { message = "'from' must be earlier than 'to'." });
        }

        var report = await _notificationService.ReconcileAsync(from.Value.ToUniversalTime(), to.Value.ToUniversalTime());

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            LocalNotificationCount = report.LocalNotificationCount,
            MatchedCount = report.MatchedCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                MessageSid = e.MessageSid,
                NotificationId = e.NotificationId,
                To = e.To,
                ProviderStatus = e.ProviderStatus,
                DateSent = e.DateSent,
                MatchStatus = e.MatchStatus
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new List<ReconciliationEntryDto>();
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>matched | missingLocally (provider knows it, eShop doesn't) | missingAtProvider (the reverse).</summary>
    public string MatchStatus { get; set; } = string.Empty;
}

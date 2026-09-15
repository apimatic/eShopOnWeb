using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }

    /// <summary>Matched, ProviderOnly, or EShopOnly.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider's records were filtered to (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int TotalProviderMessages { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator report: the provider's own record of messages sent from this application's configured number
/// over a date-time range, lined up against what eShop believes it sent — so a message one side knows about
/// and the other does not is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.ReconciliationQuery, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public class ReconciliationQuery
    {
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
                await HandleAsync(new ReconciliationQuery { From = from, To = to }, notificationService))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderNotificationService notificationService)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });
        }

        var ct = _httpContextAccessor.RequestAborted();
        var report = await notificationService.ReconcileAsync(request.From, request.To, ct);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            TotalProviderMessages = report.TotalProviderMessages,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Sid = e.Sid,
                Outcome = e.Outcome.ToString(),
                ProviderStatus = e.ProviderStatus,
                ProviderDateSent = e.ProviderDateSent,
                NotificationId = e.NotificationId,
                OrderId = e.OrderId,
                EShopStatus = e.EShopStatus
            }).ToList()
        };

        return Results.Ok(response);
    }
}

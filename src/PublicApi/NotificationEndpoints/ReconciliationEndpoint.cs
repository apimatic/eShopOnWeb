using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of this application's messages for a date range, lined up
/// against what eShop believes it sent, so a message one side knows about and the other doesn't is visible.
/// Only messages sent from this application's configured sending number are counted. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                if (from == null || to == null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
                }

                var report = await notificationService.ReconcileAsync(from.Value, to.Value, cancellationToken);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    MatchedCount = report.MatchedCount,
                    ProviderOnlyCount = report.ProviderOnlyCount,
                    EShopOnlyCount = report.EShopOnlyCount,
                    Entries = report.Entries.Select(e => new ReconciliationEntryDto
                    {
                        MessageSid = e.MessageSid,
                        Outcome = e.Outcome.ToString(),
                        ProviderStatus = e.ProviderStatus,
                        EShopStatus = e.EShopStatus,
                        NotificationId = e.NotificationId,
                        OrderId = e.OrderId,
                        ProviderDateSent = e.ProviderDateSent
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}

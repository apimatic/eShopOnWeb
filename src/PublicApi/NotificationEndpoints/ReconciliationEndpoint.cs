using System;
using System.Collections.Generic;
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
/// Operator action (administrator only): a report over a date range lining the provider's own record of
/// messages (from this app's configured sending number only) up against what eShop believes it sent, so a
/// message one side has and the other does not is visible. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, INotificationService service) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { error = "'to' must be on or after 'from'." });
                }

                var report = await service.ReconcileAsync(from, to);
                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    MatchedCount = report.Matched.Count,
                    ProviderOnlyCount = report.ProviderOnly.Count,
                    EShopOnlyCount = report.EShopOnly.Count,
                    Matched = report.Matched,
                    ProviderOnly = report.ProviderOnly,
                    EShopOnly = report.EShopOnly
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationService service) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the report is scoped to (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop have a record of, lined up by provider identifier.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider did not return for the range.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; set; } = Array.Empty<ReconciliationEntry>();
}

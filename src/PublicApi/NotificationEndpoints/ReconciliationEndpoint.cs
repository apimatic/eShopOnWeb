using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Operator report: the provider's own record of messages sent from this application's configured
/// sending number over a date range, lined up against what eShop believes it sent — so a message the
/// provider knows about and eShop doesn't, or the reverse, is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, INotificationService notifications) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                    return Results.BadRequest(new { message = "from and to are required ISO-8601 date-times." });
                if (toDate < fromDate)
                    return Results.BadRequest(new { message = "to must be on or after from." });

                var report = await notifications.ReconcileAsync(fromDate, toDate);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    MatchedCount = report.Matched.Count,
                    OnlyAtProviderCount = report.OnlyAtProvider.Count,
                    OnlyInEShopCount = report.OnlyInEShop.Count,
                    Matched = report.Matched,
                    OnlyAtProvider = report.OnlyAtProvider,
                    OnlyInEShop = report.OnlyInEShop
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was asked about (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int OnlyAtProviderCount { get; set; }
    public int OnlyInEShopCount { get; set; }

    /// <summary>Messages present both at the provider and in eShop.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = new List<ReconciliationEntry>();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public IReadOnlyList<ReconciliationEntry> OnlyAtProvider { get; set; } = new List<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider's record does not include.</summary>
    public IReadOnlyList<ReconciliationEntry> OnlyInEShop { get; set; } = new List<ReconciliationEntry>();
}

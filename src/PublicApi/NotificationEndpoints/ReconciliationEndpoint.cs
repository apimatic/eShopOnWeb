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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; set; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider did not return for the range.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; set; } = Array.Empty<ReconciliationEntry>();
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lines the provider's own record of
/// messages (for this application's sending number, over the whole range) up against what eShop
/// believes it sent, so a discrepancy either way is visible. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, HttpContext http) => await HandleAsync(from ?? string.Empty, to ?? string.Empty, http))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to, HttpContext http)
    {
        if (!TryParseIso(from, out var fromUtc) || !TryParseIso(to, out var toUtc))
        {
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
        }

        if (toUtc < fromUtc)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var service = http.RequestServices.GetRequiredService<ISmsNotificationService>();
        var report = await service.ReconcileAsync(fromUtc, toUtc, http.RequestAborted);

        var response = new ReconciliationResponse(System.Guid.NewGuid())
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            EShopOnly = report.EShopOnly,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count
        };
        return Results.Ok(response);
    }

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

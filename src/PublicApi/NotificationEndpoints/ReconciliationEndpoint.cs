using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Operator report: lists the provider's own record of messages for a date range and lines them up against
/// what eShop believes it sent. Counts only messages sent from the configured sending number. <c>from</c>
/// and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(from, to, service, http.RequestAborted))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string? from, string? to, IOrderNotificationService service, CancellationToken ct)
    {
        if (!TryParse(from, out var fromDate) || !TryParse(to, out var toDate))
            return Results.BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });
        if (toDate < fromDate)
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });

        var report = await service.ReconcileAsync(fromDate, toDate, ct);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Truncated = report.Truncated,
            Matched = Map(report.Matched),
            ProviderOnly = Map(report.ProviderOnly),
            EShopOnly = Map(report.EShopOnly),
            OutOfWindow = Map(report.OutOfWindow)
        });
    }

    private static bool TryParse(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);

    private static List<ReconciliationEntryDto> Map(IReadOnlyList<ReconciliationEntry> entries) =>
        entries.Select(e => new ReconciliationEntryDto
        {
            ProviderMessageSid = e.ProviderMessageSid,
            NotificationId = e.NotificationId,
            OrderId = e.OrderId,
            ProviderStatus = e.ProviderStatus,
            EShopState = e.EShopState,
            ProviderDateSent = e.ProviderDateSent
        }).ToList();
}

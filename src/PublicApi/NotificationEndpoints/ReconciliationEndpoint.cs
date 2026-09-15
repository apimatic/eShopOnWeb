using System;
using System.Linq;
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
/// GET /api/notifications/reconciliation?from={from}&to={to} — the provider's own record of messages for a date
/// range, lined up against what eShop believes it sent, so a message one side knows about and the other does not
/// is visible. Counts only messages sent from this application's configured sending number (the provider is asked
/// for that number's messages directly). Operator action: administrator role only. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifier) =>
                await HandleAsync(from, to, notifier))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifier)
    {
        if (to < from)
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

        var report = await notifier.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(ReconciliationEntryDto.FromEntry).ToList()
        };
        return Results.Ok(response);
    }
}

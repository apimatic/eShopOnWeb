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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reconciles the provider's own record of messages sent from the application's
/// configured sending number over a date range against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, INotificationAdminService service, CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }

                if (from > to)
                {
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });
                }

                var report = await service.ReconcileAsync(from.Value, to.Value, cancellationToken);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    ProviderMessageCount = report.ProviderMessageCount,
                    EShopMessageCount = report.EShopMessageCount,
                    MatchedCount = report.MatchedCount,
                    Matched = report.Matched.Select(ToDto).ToList(),
                    ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                    EShopOnly = report.EShopOnly.Select(ToDto).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        ProviderMessageSid = entry.ProviderMessageSid,
        ProviderStatus = entry.ProviderStatus?.ToString(),
        EShopStatus = entry.EShopStatus?.ToString(),
        NotificationId = entry.NotificationId,
        KnownToProvider = entry.KnownToProvider,
        KnownToEShop = entry.KnownToEShop
    };
}

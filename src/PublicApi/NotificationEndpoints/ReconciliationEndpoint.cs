using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — operator action. Lists the provider's
/// own record of messages from the configured sending number over the range and lines them up against
/// what eShop believes it sent, so a message one side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
                await HandleAsync(from, to, service))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var report = await service.ReconcileAsync(from, to);

        return Results.Ok(new
        {
            from = report.From,
            to = report.To,
            fromNumber = report.FromNumber,
            providerCount = report.ProviderCount,
            eShopCount = report.EShopCount,
            matchedCount = report.Matched.Count,
            providerOnlyCount = report.ProviderOnly.Count,
            eShopOnlyCount = report.EShopOnly.Count,
            matched = report.Matched.Select(ToDto),
            providerOnly = report.ProviderOnly.Select(ToDto),
            eShopOnly = report.EShopOnly.Select(ToDto)
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        NotificationId = e.NotificationId,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        DateSent = e.DateSent
    };
}

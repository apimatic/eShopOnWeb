using System;
using System.Globalization;
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
/// Operator action: lines up the provider's own record of messages sent from this
/// application's configured sending number (Twilio:FromNumber) against what eShop
/// believes it sent, over an ISO-8601 date-time range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to) =>
            {
                return await HandleAsync(from, to);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromUtc)
            || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toUtc))
        {
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
        }

        if (toUtc < fromUtc)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await _notificationService.ReconcileAsync(fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());

        return Results.Ok(new
        {
            from = report.FromUtc,
            to = report.ToUtc,
            summary = new
            {
                providerMessageCount = report.Matched.Count + report.MissingFromLocal.Count,
                localNotificationCount = report.Matched.Count + report.MissingFromProvider.Count,
                matched = report.Matched.Count,
                missingFromLocal = report.MissingFromLocal.Count,
                missingFromProvider = report.MissingFromProvider.Count,
                statusMismatches = report.Matched.Count(m => !m.StatusMatches)
            },
            matched = report.Matched.Select(m => new
            {
                notificationId = m.Notification!.Id,
                m.ProviderMessage!.MessageSid,
                localStatus = m.Notification.ProviderStatus,
                providerStatus = m.ProviderMessage.Status,
                m.StatusMatches,
                dateSent = m.ProviderMessage.DateSent
            }),
            missingFromLocal = report.MissingFromLocal.Select(p => new
            {
                p.MessageSid,
                p.Status,
                p.To,
                p.DateSent,
                p.DateCreated
            }),
            missingFromProvider = report.MissingFromProvider.Select(n => new
            {
                notificationId = n.Id,
                n.OrderId,
                messageSid = n.ProviderMessageSid,
                status = n.ProviderStatus,
                n.CreatedAt
            })
        });
    }
}

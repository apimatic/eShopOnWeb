using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's configured sending number over a date range, lined up against what eShop
/// believes it sent. The sending-number filter is applied by the provider.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IMessagingService, IRepository<OrderNotification>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IMessagingService messagingService, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(from, to, messagingService, notificationRepository);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(IMessagingService messagingService,
        IRepository<OrderNotification> notificationRepository) =>
        await HandleAsync(DateTimeOffset.MinValue, DateTimeOffset.MinValue, messagingService, notificationRepository);

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to,
        IMessagingService messagingService, IRepository<OrderNotification> notificationRepository)
    {
        if (from == DateTimeOffset.MinValue || to == DateTimeOffset.MinValue || from >= to)
        {
            return Results.BadRequest(new { error = "Query parameters 'from' and 'to' are required ISO-8601 date-times, and 'from' must be before 'to'." });
        }

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // The provider's date filter granularity is not guaranteed to honor time-of-day, so
        // query with whole-day UTC boundaries and refine to the exact range client-side.
        var dayAfter = new DateTimeOffset(fromUtc.Date, TimeSpan.Zero);
        var dayBefore = new DateTimeOffset(toUtc.Date.AddDays(1), TimeSpan.Zero);

        var providerMessages = await messagingService.ListMessagesAsync(dayAfter, dayBefore);
        if (!providerMessages.Success)
        {
            return Results.Problem("The messaging provider could not be reached for reconciliation.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var inRange = providerMessages.Messages
            .Where(m => (m.DateSent ?? DateTimeOffset.MinValue) >= fromUtc && (m.DateSent ?? DateTimeOffset.MinValue) < toUtc)
            .ToList();

        var localNotifications = await notificationRepository.ListAsync(
            new NotificationsInRangeSpecification(fromUtc, toUtc));
        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconcileNotificationsResponse
        {
            From = fromUtc,
            To = toUtc,
            Truncated = providerMessages.Truncated
        };

        foreach (var message in inRange)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                localBySid.Remove(message.Sid);
                response.Entries.Add(new ReconciliationEntryDto
                {
                    MessageSid = message.Sid,
                    Reconciliation = "matched",
                    NotificationId = local.Id,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    StatusMatch = string.Equals(message.Status, local.Status, StringComparison.OrdinalIgnoreCase),
                    To = message.To,
                    DateSent = message.DateSent
                });
            }
            else
            {
                response.Entries.Add(new ReconciliationEntryDto
                {
                    MessageSid = message.Sid,
                    Reconciliation = "providerOnly",
                    ProviderStatus = message.Status,
                    To = message.To,
                    DateSent = message.DateSent
                });
            }
        }

        foreach (var local in localBySid.Values)
        {
            response.Entries.Add(new ReconciliationEntryDto
            {
                MessageSid = local.MessageSid!,
                Reconciliation = "localOnly",
                NotificationId = local.Id,
                LocalStatus = local.Status
            });
        }

        response.MatchedCount = response.Entries.Count(e => e.Reconciliation == "matched");
        response.ProviderOnlyCount = response.Entries.Count(e => e.Reconciliation == "providerOnly");
        response.LocalOnlyCount = response.Entries.Count(e => e.Reconciliation == "localOnly");

        return Results.Ok(response);
    }
}

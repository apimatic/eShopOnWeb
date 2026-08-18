using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: a report over a date range lining up the provider's own record of messages sent
/// from this application's configured number against what eShop believes it sent, so a message one
/// side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset? from, [FromQuery(Name = "to")] DateTimeOffset? to,
             ISmsGateway smsGateway, IReadRepository<OrderNotification> notificationRepository, TwilioSettings settings) =>
            {
                return await HandleAsync(from, to, smsGateway, notificationRepository, settings);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        ISmsGateway smsGateway,
        IReadRepository<OrderNotification> notificationRepository,
        TwilioSettings settings)
    {
        if (from is null || to is null)
        {
            return Results.Problem("Both 'from' and 'to' ISO-8601 date-times are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (to.Value < from.Value)
        {
            return Results.Problem("'to' must not be earlier than 'from'.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Ask the provider for this application's own sending number's messages over the whole range.
        var providerMessages = await smsGateway.ListMessagesFromConfiguredSenderAsync(from.Value, to.Value);
        var providerInRange = providerMessages
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= from.Value && m.DateSent.Value <= to.Value)
            .GroupBy(m => m.Sid)
            .Select(g => g.First())
            .ToList();

        // What eShop believes it actually sent in the window: notifications with a provider SID whose
        // send was handed off (not merely scheduled, cancelled, or failed to leave this app).
        var localAll = await notificationRepository.ListAsync();
        var localSentInRange = localAll
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)
                && n.CreatedDate >= from.Value && n.CreatedDate <= to.Value
                && (n.Outcome == NotificationDeliveryOutcome.InFlight
                    || n.Outcome == NotificationDeliveryOutcome.Reached
                    || n.Outcome == NotificationDeliveryOutcome.NotReached))
            .ToList();

        var localBySid = new Dictionary<string, OrderNotification>();
        foreach (var n in localSentInRange)
        {
            localBySid[n.ProviderMessageSid!] = n;
        }

        var providerSids = new HashSet<string>(providerInRange.Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var m in providerInRange)
        {
            if (localBySid.TryGetValue(m.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = m.Sid,
                    ProviderStatus = m.Status,
                    DateSent = m.DateSent,
                    NotificationId = local.Id,
                    EShopStatus = local.ProviderStatus
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = m.Sid,
                    ProviderStatus = m.Status,
                    DateSent = m.DateSent
                });
            }
        }

        var eShopOnly = localSentInRange
            .Where(n => !providerSids.Contains(n.ProviderMessageSid!))
            .Select(n => new ReconciliationEntry
            {
                ProviderMessageSid = n.ProviderMessageSid,
                NotificationId = n.Id,
                EShopStatus = n.ProviderStatus,
                DateSent = null
            })
            .ToList();

        var response = new ReconciliationResponse
        {
            From = from.Value,
            To = to.Value,
            FromNumber = settings.FromNumber,
            ProviderCount = providerInRange.Count,
            EShopCount = localSentInRange.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
        return Results.Ok(response);
    }
}

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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages sent from this application's configured sending
/// number over a date range, and lines them up against what eShop believes it sent — so a message the provider
/// knows about and eShop doesn't (or the reverse) is visible. The provider is asked for that number's messages
/// directly, so traffic on the account that is not this application's is never counted.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReadRepository<Notification> notificationRepository, ISmsProvider smsProvider, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, notificationRepository, smsProvider, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        string? from,
        string? to,
        IReadRepository<Notification> notificationRepository,
        ISmsProvider smsProvider,
        CancellationToken ct)
    {
        if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
        {
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times." });
        }
        if (fromDate > toDate)
        {
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await smsProvider.ListOwnMessagesAsync(fromDate, toDate, ct);
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.From(ex);
        }

        var eShopSent = await notificationRepository.ListAsync(new SentNotificationsInRangeSpecification(fromDate, toDate), ct);

        // Line the two records up by the provider's message SID.
        var eShopBySid = eShopSent
            .Where(n => n.ProviderSid is not null)
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!));

        var response = new ReconciliationResponse
        {
            From = fromDate,
            To = toDate,
            FromNumberMasked = PhoneMask.Mask(providerMessages.FirstOrDefault(m => m.From is not null)?.From),
            ProviderCount = providerMessages.Count,
            EShopCount = eShopSent.Count
        };

        foreach (var m in providerMessages)
        {
            var entry = new ReconciliationProviderEntry
            {
                ProviderSid = m.Sid,
                Status = m.Status,
                ToMasked = PhoneMask.Mask(m.To),
                DateSent = m.DateSent
            };

            if (m.Sid is not null && eShopBySid.TryGetValue(m.Sid, out var matchedLocal))
            {
                entry.NotificationId = matchedLocal.Id;
                entry.OrderId = matchedLocal.OrderId;
                response.Matched.Add(entry);
            }
            else
            {
                // The provider knows about this message but eShop has no record of it.
                response.ProviderOnly.Add(entry);
            }
        }

        foreach (var n in eShopSent)
        {
            if (n.ProviderSid is null || !providerSids.Contains(n.ProviderSid))
            {
                // eShop believes it sent this, but the provider's record for the range does not include it.
                response.EShopOnly.Add(new ReconciliationEShopEntry
                {
                    NotificationId = n.Id,
                    OrderId = n.OrderId,
                    ProviderSid = n.ProviderSid,
                    Status = n.DeliveryStatus
                });
            }
        }

        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out result);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string? FromNumberMasked { get; set; }

    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;

    /// <summary>Messages present in both the provider's record and eShop's.</summary>
    public List<ReconciliationProviderEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationProviderEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record for the range does not include.</summary>
    public List<ReconciliationEShopEntry> EShopOnly { get; set; } = new();
}

public class ReconciliationProviderEntry
{
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? ToMasked { get; set; }
    public string? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
}

public class ReconciliationEShopEntry
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
}

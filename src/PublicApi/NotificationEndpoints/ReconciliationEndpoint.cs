using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntry
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }

    /// <summary>matched | missingLocally (provider knows it, eShop does not) | missingAtProvider (eShop sent it, provider has no record in range).</summary>
    public string Disposition { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: lines up the provider's own record of messages sent from this
/// application's configured sending number (Twilio:FromNumber) in a date range against
/// what eShop believes it sent. The provider performs the From/date filtering; the whole
/// range is covered by following the provider's paging.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, HttpContext>
{
    private readonly IMessagingProvider _messagingProvider;
    private readonly IRepository<OrderNotification> _notificationRepository;

    public ReconciliationEndpoint(IMessagingProvider messagingProvider,
        IRepository<OrderNotification> notificationRepository)
    {
        _messagingProvider = messagingProvider;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext httpContext) =>
            {
                return await HandleAsync(from, to, httpContext);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, HttpContext httpContext)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _messagingProvider.ListMessagesAsync(from, to, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            return Results.Json(new { message = $"The provider's records could not be retrieved: {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), httpContext.RequestAborted);

        // Scheduled messages have not been sent yet and carry no DateSent at the provider,
        // so they cannot appear in the provider's range answer; exclude them locally too.
        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null && n.Status != OrderNotificationStatuses.Scheduled)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerBySid = providerMessages.ToDictionary(m => m.ProviderMessageSid, m => m);

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        foreach (var sid in providerBySid.Keys.Union(localBySid.Keys).OrderBy(s => s))
        {
            providerBySid.TryGetValue(sid, out var providerMessage);
            localBySid.TryGetValue(sid, out var localNotification);

            response.Entries.Add(new ReconciliationEntry
            {
                ProviderMessageSid = sid,
                ProviderStatus = providerMessage?.Status,
                ProviderDateSent = providerMessage?.DateSent,
                NotificationId = localNotification?.Id,
                LocalStatus = localNotification?.Status,
                Disposition = providerMessage is not null && localNotification is not null
                    ? "matched"
                    : providerMessage is not null ? "missingLocally" : "missingAtProvider"
            });
        }

        return Results.Ok(response);
    }
}

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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Middleware;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator action): the provider's own record of messages sent
/// from this application's configured sending number over a date range, lined up against
/// what eShop believes it sent. Anything on only one side — or with a differing outcome —
/// is listed. The provider is asked for that number's messages directly, so traffic from
/// other applications sharing the account is never in the answer.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint
{
    private readonly IMessagingService _messagingService;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly TwilioSettings _settings;

    public ReconcileNotificationsEndpoint(
        IMessagingService messagingService,
        IRepository<OrderNotification> notificationRepository,
        TwilioSettings settings)
    {
        _messagingService = messagingService;
        _notificationRepository = notificationRepository;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, ct);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (from is null || to is null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' are required, as ISO-8601 date-times." });
        }

        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _messagingService.ListMessagesFromSenderAsync(from.Value, to.Value, ct);
        }
        catch (MessagingException ex)
        {
            return ProviderErrorResults.Map(ex);
        }

        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedBetweenSpecification(from.Value, to.Value), ct);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerBySid = providerMessages
            .Where(p => p.Sid != null)
            .GroupBy(p => p.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse
        {
            From = from.Value,
            To = to.Value,
            SendingNumber = _settings.FromNumber ?? string.Empty,
            ProviderMessageCount = providerBySid.Count,
            LocalMessageCount = localNotifications.Count
        };

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (!localBySid.ContainsKey(sid))
            {
                response.MissingFromLocal.Add(new ProviderMessageDto
                {
                    MessageSid = sid,
                    To = providerMessage.To ?? string.Empty,
                    Status = providerMessage.Status ?? string.Empty,
                    ErrorCode = providerMessage.ErrorCode,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.MessageSid is null || !providerBySid.TryGetValue(local.MessageSid, out var providerMessage))
            {
                response.MissingFromProvider.Add(new LocalMessageDto
                {
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    MessageSid = local.MessageSid,
                    Kind = local.Kind.ToString(),
                    Status = local.Status
                });
                continue;
            }

            response.MatchedCount++;
            var providerStatus = providerMessage.Status ?? string.Empty;
            if (!string.Equals(local.Status, providerStatus, StringComparison.OrdinalIgnoreCase))
            {
                response.StatusMismatches.Add(new StatusMismatchDto
                {
                    NotificationId = local.Id,
                    MessageSid = local.MessageSid,
                    LocalStatus = local.Status,
                    ProviderStatus = providerStatus
                });
            }
        }

        return Results.Ok(response);
    }
}

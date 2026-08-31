using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's sending number over a date range, lined up against what eShop believes it
/// sent. Messages the provider knows about and eShop doesn't — and the reverse — are visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<OrderNotification>, ISmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
                IRepository<OrderNotification> notificationRepository, ISmsService smsService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notificationRepository, smsService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<OrderNotification> notificationRepository, ISmsService smsService)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new { error = "'to' must be later than 'from'." });
        }

        try
        {
            var providerMessages = await smsService.ListMessagesAsync(request.From, request.To);
            var localNotifications = await notificationRepository.ListAsync(
                new OrderNotificationsCreatedInRangeSpecification(request.From, request.To));

            var localBySid = localNotifications
                .Where(n => n.MessageSid != null)
                .GroupBy(n => n.MessageSid!)
                .ToDictionary(g => g.Key, g => g.First());
            var providerSids = providerMessages.Select(m => m.Sid).ToHashSet();

            var response = new ReconciliationResponse
            {
                From = request.From,
                To = request.To,
                Matched = providerMessages
                    .Where(m => localBySid.ContainsKey(m.Sid))
                    .Select(m =>
                    {
                        var local = localBySid[m.Sid];
                        return new ReconciledMessageDto
                        {
                            NotificationId = local.Id,
                            MessageSid = m.Sid,
                            ProviderStatus = m.Status,
                            LocalStatus = local.LastKnownStatus,
                            StatusMismatch = m.Status != null
                                && !string.Equals(m.Status, local.LastKnownStatus, StringComparison.OrdinalIgnoreCase),
                            DateSent = m.DateSent
                        };
                    }).ToList(),
                OnlyAtProvider = providerMessages
                    .Where(m => !localBySid.ContainsKey(m.Sid))
                    .Select(m => new ProviderOnlyMessageDto
                    {
                        MessageSid = m.Sid,
                        To = m.To,
                        Status = m.Status,
                        DateSent = m.DateSent
                    }).ToList(),
                OnlyInShop = localNotifications
                    .Where(n => n.MessageSid == null || !providerSids.Contains(n.MessageSid))
                    .Select(n => new ShopOnlyMessageDto
                    {
                        NotificationId = n.Id,
                        OrderId = n.OrderId,
                        Type = n.Type.ToString(),
                        MessageSid = n.MessageSid,
                        LocalStatus = n.LastKnownStatus,
                        CreatedAt = n.CreatedAt
                    }).ToList()
            };

            return Results.Ok(response);
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciledMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public bool StatusMatch { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
}

public class LocalOnlyMessageDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalMessageCount { get; set; }
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<LocalOnlyMessageDto> LocalOnly { get; set; } = new();
}

/// <summary>
/// Lines up the provider's own record of messages for a date range against what eShop
/// believes it sent (operator). Only traffic from this application's configured sending
/// number is requested from the provider.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly ISmsService _smsService;
    private readonly IRepository<Notification> _notificationRepository;

    public ReconciliationEndpoint(ISmsService smsService, IRepository<Notification> notificationRepository)
    {
        _smsService = smsService;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var providerMessages = await _smsService.ListMessagesAsync(from, to);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpecification(from, to));

        var localBySid = localNotifications
            .GroupBy(n => n.MessageSid)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.UpdatedAt).First());

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalMessageCount = localNotifications.Count
        };

        var matchedSids = new HashSet<string>();
        foreach (var providerMessage in providerMessages)
        {
            if (localBySid.TryGetValue(providerMessage.MessageSid, out var local))
            {
                matchedSids.Add(providerMessage.MessageSid);
                response.Matched.Add(new ReconciledMessageDto
                {
                    MessageSid = providerMessage.MessageSid,
                    NotificationId = local.Id,
                    ProviderStatus = providerMessage.Status,
                    LocalStatus = local.Status,
                    StatusMatch = string.Equals(providerMessage.Status, local.Status, StringComparison.OrdinalIgnoreCase),
                    DateSent = providerMessage.DateSent
                });
            }
            else
            {
                response.ProviderOnly.Add(new ProviderOnlyMessageDto
                {
                    MessageSid = providerMessage.MessageSid,
                    To = providerMessage.To,
                    Status = providerMessage.Status,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        foreach (var local in localNotifications.Where(n => !matchedSids.Contains(n.MessageSid)))
        {
            response.LocalOnly.Add(new LocalOnlyMessageDto
            {
                NotificationId = local.Id,
                MessageSid = local.MessageSid,
                Status = local.Status,
                CreatedAt = local.CreatedAt
            });
        }

        return Results.Ok(response);
    }
}

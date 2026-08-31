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
using Microsoft.eShopWeb.ApplicationCore.Models.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reconciles the provider's own record of messages for a date range
/// (limited server-side to this application's configured sending number) against what
/// eShop believes it sent, surfacing entries present on only one side.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<OrderNotification> notificationRepository, ISmsNotificationClient smsClient) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notificationRepository, smsClient);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationClient smsClient)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "'from' must not be later than 'to'." });
        }

        IReadOnlyList<ProviderMessageRecord> providerMessages;
        try
        {
            providerMessages = await smsClient.ListMessagesAsync(request.From, request.To);
        }
        catch (TwilioApiException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        var localNotifications = await notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(request.From, request.To));

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.MessageSid))
            .GroupBy(m => m.MessageSid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciledMessageDto>();
        foreach (var (sid, local) in localBySid)
        {
            if (!providerBySid.TryGetValue(sid, out var provider))
            {
                continue;
            }
            matched.Add(new ReconciledMessageDto
            {
                NotificationId = local.Id,
                MessageSid = sid,
                LocalStatus = local.Status,
                ProviderStatus = provider.Status,
                StatusMismatch = !string.Equals(local.Status, provider.Status, StringComparison.OrdinalIgnoreCase)
            });
        }

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            Matched = matched,
            ProviderOnly = providerBySid
                .Where(pair => !localBySid.ContainsKey(pair.Key))
                .Select(pair => new ProviderOnlyMessageDto
                {
                    MessageSid = pair.Key,
                    To = pair.Value.To,
                    Status = pair.Value.Status,
                    DateSent = pair.Value.DateSent,
                    DateCreated = pair.Value.DateCreated
                }).ToList(),
            LocalOnly = localNotifications
                .Where(n => string.IsNullOrEmpty(n.MessageSid) || !providerBySid.ContainsKey(n.MessageSid!))
                .Select(n => new LocalOnlyMessageDto
                {
                    NotificationId = n.Id,
                    MessageSid = n.MessageSid,
                    OrderId = n.OrderId,
                    Type = n.Type.ToString(),
                    Status = n.Status,
                    CreatedAt = n.CreatedAt
                }).ToList()
        };

        response.Summary = new ReconciliationSummary
        {
            ProviderCount = providerBySid.Count,
            LocalCount = localNotifications.Count,
            MatchedCount = response.Matched.Count,
            ProviderOnlyCount = response.ProviderOnly.Count,
            LocalOnlyCount = response.LocalOnly.Count
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationSummary Summary { get; set; } = new();
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<LocalOnlyMessageDto> LocalOnly { get; set; } = new();
}

public class ReconciliationSummary
{
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
}

public class ReconciledMessageDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public bool StatusMismatch { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class LocalOnlyMessageDto
{
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

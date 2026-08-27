using System;
using System.Collections.Generic;
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
/// application's sending number against what eShop believes it sent, for a date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, INotificationReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationReconciliationService reconciliationService)
    {
        if (request.From == null || request.To == null)
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' (ISO-8601 date-times) are required." });
        }

        if (request.To < request.From)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var report = await reconciliationService.ReconcileAsync(request.From.Value, request.To.Value);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            LocalNotificationCount = report.LocalNotificationCount,
            Matched = report.Matched.Select(m => new ReconciliationMatchedDto
            {
                NotificationId = m.NotificationId,
                ProviderMessageSid = m.ProviderMessageSid,
                ProviderStatus = m.ProviderStatus,
                LocalStatus = m.LocalStatus,
                StatusMismatch = m.StatusMismatch,
                DateSent = m.DateSent
            }).ToList(),
            OnlyInProvider = report.OnlyInProvider.Select(p => new ReconciliationProviderMessageDto
            {
                ProviderMessageSid = p.ProviderMessageSid,
                Status = p.Status,
                To = p.To,
                DateSent = p.DateSent,
                DateCreated = p.DateCreated
            }).ToList(),
            OnlyInEShop = report.OnlyInEShop.Select(l => new ReconciliationLocalDto
            {
                NotificationId = l.NotificationId,
                ProviderMessageSid = l.ProviderMessageSid,
                LocalStatus = l.LocalStatus,
                CreatedAt = l.CreatedAt
            }).ToList()
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }

    public ReconciliationRequest(DateTimeOffset? from, DateTimeOffset? to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationMatchedDto> Matched { get; set; } = new();
    public List<ReconciliationProviderMessageDto> OnlyInProvider { get; set; } = new();
    public List<ReconciliationLocalDto> OnlyInEShop { get; set; } = new();
}

public class ReconciliationMatchedDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public bool StatusMismatch { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationProviderMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? To { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationLocalDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

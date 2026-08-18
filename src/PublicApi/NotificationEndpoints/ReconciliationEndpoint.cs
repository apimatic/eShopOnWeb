using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lists the provider's own record of messages sent from this application's configured
/// number over an ISO-8601 date range and lines them up against what eShop believes it sent, so a message
/// one side knows about and the other doesn't is visible. Administrator role required.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset from, [FromQuery(Name = "to")] DateTimeOffset to,
             INotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, service, ct);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, INotificationService service, CancellationToken ct)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await service.ReconcileAsync(from, to, ct);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciledMatchDto
            {
                NotificationId = m.Notification.Id,
                OrderId = m.Notification.OrderId,
                Kind = m.Notification.Kind.ToString(),
                MessageSid = m.Notification.MessageSid,
                EShopStatus = m.Notification.Status,
                ProviderStatus = m.ProviderMessage.Status,
                ProviderDateSent = m.ProviderMessage.DateSent,
                ProviderErrorCode = m.ProviderMessage.ErrorCode
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderMessageDto
            {
                MessageSid = p.Sid,
                Status = p.Status,
                DateSent = p.DateSent,
                ErrorCode = p.ErrorCode
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(n => new EShopOnlyDto
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                Kind = n.Kind.ToString(),
                MessageSid = n.MessageSid,
                Status = n.Status
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciledMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<EShopOnlyDto> EShopOnly { get; set; } = new();
}

public class ReconciledMatchDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string? EShopStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? ProviderErrorCode { get; set; }
}

public class ProviderMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
}

public class EShopOnlyDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
}

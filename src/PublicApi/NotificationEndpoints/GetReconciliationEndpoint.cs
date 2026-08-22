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

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationProviderOnlyDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEshopOnlyDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public string ProviderSid { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class ReconciliationProviderOnlyDto
{
    public string ProviderSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationEshopOnlyDto
{
    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
}

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService orders) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), orders);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService orders)
    {
        var report = await orders.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                NotificationId = m.NotificationId,
                ProviderSid = m.ProviderSid,
                Status = m.Status
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ReconciliationProviderOnlyDto
            {
                ProviderSid = p.ProviderSid,
                Status = p.Status,
                DateCreated = p.DateCreated
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new ReconciliationEshopOnlyDto
            {
                NotificationId = e.NotificationId,
                ProviderSid = e.ProviderSid,
                Status = e.Status
            }).ToList()
        };
        return Results.Ok(response);
    }
}

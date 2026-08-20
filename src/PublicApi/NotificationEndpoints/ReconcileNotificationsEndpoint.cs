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

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int LocalCount { get; set; }
    public int MatchedCount { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationMismatchDto> Mismatches { get; set; } = new();
}

public class ReconciliationMismatchDto
{
    public string? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DateSent { get; set; }
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(service, from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) =>
        HandleAsync(service, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

    private async Task<IResult> HandleAsync(IOrderNotificationService service, DateTimeOffset from, DateTimeOffset to)
    {
        var report = await service.ReconcileAsync(from, to, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            LocalCount = report.LocalCount,
            MatchedCount = report.MatchedCount,
            Truncated = report.Truncated,
            Mismatches = report.Mismatches.Select(m => new ReconciliationMismatchDto
            {
                NotificationId = m.NotificationId,
                ProviderSid = m.ProviderSid,
                Source = m.Source,
                Status = m.Status,
                DateSent = m.DateSent
            }).ToList()
        });
    }
}

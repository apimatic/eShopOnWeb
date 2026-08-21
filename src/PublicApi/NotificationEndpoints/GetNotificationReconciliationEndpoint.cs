using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationProviderMessageDto
{
    public string ProviderSid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? DateSent { get; init; }
}

public class ReconciliationMatchedDto
{
    public NotificationDto Local { get; init; } = new();
    public ReconciliationProviderMessageDto Provider { get; init; } = new();
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public List<ReconciliationMatchedDto> Matched { get; init; } = new();
    public List<ReconciliationProviderMessageDto> ProviderOnly { get; init; } = new();
    public List<NotificationDto> LocalOnly { get; init; } = new();
}

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notifications);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciliationMatchedDto
            {
                Local = NotificationDto.From(m.Local),
                Provider = MapProvider(m.Provider)
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(MapProvider).ToList(),
            LocalOnly = report.LocalOnly.Select(NotificationDto.From).ToList()
        });
    }

    private static ReconciliationProviderMessageDto MapProvider(SmsMessageSnapshot snapshot)
    {
        return new ReconciliationProviderMessageDto
        {
            ProviderSid = snapshot.Sid,
            Status = snapshot.Status,
            DateSent = snapshot.DateSent
        };
    }
}

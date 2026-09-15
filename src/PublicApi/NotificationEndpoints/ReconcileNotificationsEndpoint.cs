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

public class ReconcileNotificationsRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationItemDto
{
    public string? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? LocalCreatedAt { get; set; }
    public string? Kind { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationItemDto> Items { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), service);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconcileNotificationsResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            LocalOnlyCount = report.LocalOnlyCount,
            Items = report.Items.Select(i => new ReconciliationItemDto
            {
                NotificationId = i.NotificationId,
                ProviderMessageSid = i.ProviderMessageSid,
                Match = i.Match,
                ProviderStatus = i.ProviderStatus,
                ProviderDateSent = i.ProviderDateSent,
                LocalCreatedAt = i.LocalCreatedAt,
                Kind = i.Kind?.ToString()
            }).ToList()
        };
        return Results.Ok(response);
    }
}

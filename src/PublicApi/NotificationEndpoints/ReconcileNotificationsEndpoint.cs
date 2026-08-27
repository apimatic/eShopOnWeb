using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), notifications);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(request.From, request.To);
        var response = new ReconcileNotificationsResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = 0
        };

        foreach (var item in report.Items)
        {
            var dto = new ReconciliationItemDto
            {
                Match = item.Match,
                NotificationId = item.NotificationId,
                ProviderMessageSid = item.ProviderMessageSid,
                ProviderStatus = item.ProviderStatus,
                EShopStatus = item.EShopStatus,
                DateSent = item.DateSent
            };
            response.Items.Add(dto);
            if (item.Match == "matched")
            {
                response.MatchedCount++;
            }
        }

        return Results.Ok(response);
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ReconcileNotificationsResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationItemDto> Items { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string Match { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

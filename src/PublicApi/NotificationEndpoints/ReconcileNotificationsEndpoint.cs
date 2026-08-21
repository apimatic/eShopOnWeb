using System;
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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(from, to, notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, IOrderNotificationService notifications)
    {
        throw new NotSupportedException();
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications)
    {
        try
        {
            var report = await notifications.ReconcileAsync(from, to);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Matched = report.Matched.Select(Map).ToList(),
                ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                ApplicationOnly = report.ApplicationOnly.Select(Map).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static ReconciliationItemDto Map(ReconciledNotification item)
    {
        return new ReconciliationItemDto
        {
            NotificationId = item.NotificationId,
            ProviderMessageSid = item.ProviderMessageSid,
            Status = item.Status,
            OrderId = item.OrderId,
            DateSent = item.DateSent,
            DateCreated = item.DateCreated
        };
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public System.Collections.Generic.List<ReconciliationItemDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationItemDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

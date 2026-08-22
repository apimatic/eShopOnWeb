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
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderSmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderSmsNotificationService notifications) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromValue) || !DateTimeOffset.TryParse(to, out var toValue))
                {
                    return Results.BadRequest("from and to must be ISO-8601 date-times.");
                }

                return await HandleAsync(new ReconcileNotificationsRequest { From = fromValue, To = toValue }, notifications);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderSmsNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconcileNotificationsResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(NotificationReconciliationEntry entry) =>
        new()
        {
            ProviderMessageSid = entry.ProviderMessageSid,
            NotificationId = entry.NotificationId,
            ProviderStatus = entry.ProviderStatus,
            ApplicationStatus = entry.ApplicationStatus,
            Source = entry.Source
        };
}

using System;
using System.Globalization;
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

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationService notificationService) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue) ||
                    !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                if (toValue < fromValue)
                {
                    return Results.BadRequest(new { message = "to must be greater than or equal to from." });
                }

                var report = await notificationService.ReconcileAsync(fromValue, toValue);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    MatchedCount = report.MatchedCount,
                    ProviderOnlyCount = report.ProviderOnlyCount,
                    LocalOnlyCount = report.LocalOnlyCount,
                    Items = report.Items.Select(i => new ReconciliationItemDto
                    {
                        ProviderMessageSid = i.ProviderMessageSid,
                        NotificationId = i.LocalNotificationId,
                        ProviderStatus = i.ProviderStatus,
                        LocalStatus = i.LocalStatus,
                        Match = i.Match
                    }).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService) =>
        throw new System.NotSupportedException();
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public System.Collections.Generic.List<ReconciliationItemDto> Items { get; set; } = new();
}

public class ReconciliationItemDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}

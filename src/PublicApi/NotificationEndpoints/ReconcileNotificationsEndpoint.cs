using System;
using System.Collections.Generic;
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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, notificationService);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService notificationService)
    {
        if (!TryParseTimestamp(request.From, out var from))
        {
            return Results.BadRequest(new { message = "Query parameter 'from' must be an ISO-8601 date-time." });
        }

        if (!TryParseTimestamp(request.To, out var to))
        {
            return Results.BadRequest(new { message = "Query parameter 'to' must be an ISO-8601 date-time." });
        }

        try
        {
            var report = await notificationService.ReconcileAsync(from, to);
            return Results.Ok(new ReconcileNotificationsResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Matched = report.Matched.ToList(),
                ProviderOnly = report.ProviderOnly.ToList(),
                ApplicationOnly = report.ApplicationOnly.ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp);
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledMessage> Matched { get; set; } = new();
    public List<ReconciledMessage> ProviderOnly { get; set; } = new();
    public List<ReconciledMessage> ApplicationOnly { get; set; } = new();
}

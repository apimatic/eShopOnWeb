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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), notifications);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService notifications)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' are required as ISO-8601 date-times." });
        }

        try
        {
            var report = await notifications.ReconcileAsync(request.From.Value, request.To.Value);
            return Results.Ok(new ReconcileNotificationsResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                MatchedCount = report.Matched.Count,
                ProviderOnlyCount = report.ProviderOnly.Count,
                LocalOnlyCount = report.LocalOnly.Count,
                Matched = report.Matched.Select(m => new ReconciledMessageDto
                {
                    NotificationId = m.NotificationId,
                    ProviderMessageSid = m.ProviderMessageSid,
                    LocalStatus = m.LocalStatus,
                    ProviderStatus = m.ProviderStatus
                }).ToList(),
                ProviderOnly = report.ProviderOnly.Select(m => new ProviderOnlyMessageDto
                {
                    ProviderMessageSid = m.ProviderMessageSid,
                    ProviderStatus = m.ProviderStatus,
                    DateSent = m.DateSent
                }).ToList(),
                LocalOnly = report.LocalOnly.Select(m => new LocalOnlyMessageDto
                {
                    NotificationId = m.NotificationId,
                    ProviderMessageSid = m.ProviderMessageSid,
                    LocalStatus = m.LocalStatus
                }).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public record ReconcileNotificationsRequest(DateTimeOffset? From, DateTimeOffset? To);

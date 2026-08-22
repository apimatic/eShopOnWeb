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

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(from, to, notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notifications)
        => HandleAsync(DateTimeOffset.MinValue, DateTimeOffset.MinValue, notifications);

    private async Task<IResult> HandleAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IOrderNotificationService notifications)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "`to` must be on or after `from`." });
        }

        var report = await notifications.ReconcileAsync(from, to, default);
        var response = new ReconciliationResponse
        {
            From = report.From.ToString("O"),
            To = report.To.ToString("O"),
            FromNumber = report.FromNumber,
            Complete = report.Complete,
            Entries = report.Entries.Select(e => new ReconciliationItemDto
            {
                ProviderSid = e.ProviderSid,
                Status = e.Status,
                From = e.From,
                Body = e.Body,
                DateSent = e.DateSent,
                DateCreated = e.DateCreated,
                NotificationId = e.LocalNotificationId,
                InProvider = e.InProvider,
                InApplication = e.InApplication
            }).ToArray()
        };

        return Results.Ok(response);
    }
}

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IOrderNotificationService _notifications;

    public ReconciliationEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (from > to)
        {
            return Results.BadRequest(new { message = "from must be earlier than or equal to to." });
        }

        try
        {
            var report = await _notifications.ReconcileAsync(from, to, default);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                SendingNumber = report.SendingNumber,
                Truncated = report.Truncated,
                Matched = report.Matched.Select(ToDto).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                EshopOnly = report.EshopOnly.Select(ToDto).ToList()
            });
        }
        catch (MessagingProviderException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ReconciliationItemDto ToDto(ReconciledMessage item) => new()
    {
        NotificationId = item.NotificationId,
        ProviderSid = item.ProviderSid,
        Status = item.Status,
        DateSent = item.DateSent
    };
}

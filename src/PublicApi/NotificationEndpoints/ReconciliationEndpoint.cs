using System;
using System.Linq;
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

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), notifications);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderNotificationService notifications)
    {
        try
        {
            var report = await notifications.ReconcileAsync(request.From, request.To, default);
            return Results.Ok(new
            {
                from = report.From,
                to = report.To,
                truncated = report.Truncated,
                entries = report.Entries.Select(e => new
                {
                    providerSid = e.ProviderSid,
                    status = e.Status,
                    from = e.From,
                    dateSent = e.DateSent,
                    notificationId = e.EshopNotificationId,
                    inProvider = e.InProvider,
                    inEshop = e.InEshop,
                    matched = e.InProvider && e.InEshop,
                    providerOnly = e.InProvider && !e.InEshop,
                    eshopOnly = e.InEshop && !e.InProvider
                }).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (MessagingProviderException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

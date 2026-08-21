using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, INotificationOperatorService notifications) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), notifications);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, INotificationOperatorService notifications)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) ||
            !DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new OrderNotificationException(400, "'from' and 'to' must be ISO-8601 date-times.");
        }

        var report = await notifications.ReconcileAsync(from, to);
        return Results.Ok(report);
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

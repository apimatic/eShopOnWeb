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

/// <summary>
/// Operator: line up Twilio's record of messages from Twilio:FromNumber with what eShop stored.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), service);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconcileNotificationsResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.ToList(),
            ProviderOnly = report.ProviderOnly.ToList(),
            ApplicationOnly = report.ApplicationOnly.ToList()
        };
        return Results.Ok(response);
    }
}

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

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from this
/// application's sending number over a date range, lined up against what eShop believes it
/// sent. ISO-8601 date-times; the whole range is covered.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationService notificationService) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, notificationService);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, INotificationService notificationService)
    {
        if (request.To <= request.From)
        {
            throw new BadRequestException("'to' must be after 'from'.");
        }

        var response = new ReconcileNotificationsResponse(request.CorrelationId());

        var result = await notificationService.ReconcileAsync(request.From.ToUniversalTime(), request.To.ToUniversalTime());

        response.From = result.FromUtc;
        response.To = result.ToUtc;
        response.FromNumber = result.FromNumber;
        response.ProviderListTruncated = result.ProviderListTruncated;
        response.Matched = result.Matched.Select(m => new ReconciledNotificationDto
        {
            NotificationId = m.NotificationId,
            MessageSid = m.MessageSid,
            AppStatus = m.AppStatus,
            ProviderStatus = m.ProviderStatus,
            StatusMatches = m.StatusMatches
        }).ToList();
        response.ProviderOnly = result.ProviderOnly.Select(p => new ProviderMessageDto
        {
            MessageSid = p.MessageSid,
            To = p.To,
            From = p.From,
            Status = p.Status,
            DateSent = p.DateSent,
            ErrorCode = p.ErrorCode,
            ErrorMessage = p.ErrorMessage
        }).ToList();
        response.AppOnly = result.AppOnly.Select(n => new AppOnlyNotificationDto
        {
            NotificationId = n.Id,
            OrderId = n.OrderId,
            Type = n.Type.ToString(),
            Status = n.Status,
            MessageSid = n.ProviderMessageSid,
            CreatedAt = n.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}

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

/// <summary>
/// Operator report: lists the provider's own record of messages sent from this application's
/// configured sending number over a date range and lines them up against what eShop believes it
/// sent, so a message one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Request, IOrderNotificationService>
{
    public record Request(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                if (!TryParseIso(from, out var fromValue) || !TryParseIso(to, out var toValue))
                {
                    return Results.BadRequest(new { message = "'from' and 'to' are required ISO-8601 date-times." });
                }

                if (toValue < fromValue)
                {
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
                }

                return await HandleAsync(new Request(fromValue, toValue), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopRecordCount = report.EShopRecordCount,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                ProviderMessageSid = m.ProviderMessageSid,
                ProviderStatus = m.ProviderStatus,
                NotificationId = m.NotificationId,
                OrderId = m.OrderId,
                Kind = m.Kind.ToString(),
                EShopStatus = m.EShopStatus.ToString()
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderMessageDto
            {
                ProviderMessageSid = p.ProviderMessageSid,
                ProviderStatus = p.ProviderStatus,
                DateSent = p.DateSent,
                ErrorCode = p.ErrorCode
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(e => new EShopNotificationDto
            {
                NotificationId = e.NotificationId,
                ProviderMessageSid = e.ProviderMessageSid,
                OrderId = e.OrderId,
                Kind = e.Kind.ToString(),
                EShopStatus = e.EShopStatus.ToString(),
                CreatedAt = e.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

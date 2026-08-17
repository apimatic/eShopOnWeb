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
/// Operator action: reconciles the provider's record of messages sent from the configured sending
/// number over a date range against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationManagementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationManagementService service) =>
                await HandleAsync(new ReconciliationRequest(from, to), service))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationManagementService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EShopOnly = report.EShopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        ProviderMessageId = e.ProviderMessageId,
        To = e.MaskedTo,
        ProviderStatus = e.ProviderStatus,
        ErrorCode = e.ErrorCode,
        DateSent = e.DateSent,
        NotificationId = e.NotificationId,
        EShopStatus = e.EShopStatus
    };
}

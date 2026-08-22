using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
    {
        try
        {
            var report = await notifications.ReconcileAsync(request.From, request.To);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Matched = report.Matched.Select(Map).ToList(),
                ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                EShopOnly = report.EShopOnly.Select(Map).ToList()
            });
        }
        catch (NotificationActionException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            ProviderMessageSid = entry.ProviderMessageSid,
            NotificationId = entry.NotificationId,
            ProviderStatus = entry.ProviderStatus,
            EShopStatus = entry.EShopStatus,
            ProviderDateSent = entry.ProviderDateSent,
            EShopCreatedAt = entry.EShopCreatedAt
        };
    }
}

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? EShopCreatedAt { get; set; }
}

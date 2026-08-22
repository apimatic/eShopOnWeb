using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconcileNotificationsResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Items { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, notificationService);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ReconcileNotificationsRequest request,
        IOrderNotificationService notificationService)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest("The 'to' timestamp must be on or after 'from'.");
        }

        var items = await notificationService.ReconcileAsync(request.From, request.To);
        var response = new ReconcileNotificationsResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            MatchedCount = items.Count(i => i.Source == "matched"),
            EshopOnlyCount = items.Count(i => i.Source == "eshop_only"),
            ProviderOnlyCount = items.Count(i => i.Source == "provider_only"),
            Items = items.Select(i => new ReconciliationEntryDto
            {
                ProviderMessageSid = i.ProviderMessageSid,
                NotificationId = i.NotificationId,
                Source = i.Source,
                Status = i.ProviderStatus
            }).ToList()
        };

        return Results.Ok(response);
    }
}

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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notificationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var result = await notificationService.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = result.From,
            To = result.To,
            FromNumber = result.FromNumber,
            Matched = result.Matched.Select(ToDto).ToList(),
            ProviderOnly = result.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = result.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry) => new()
    {
        ProviderMessageSid = entry.ProviderMessageSid,
        ProviderStatus = entry.ProviderStatus,
        DateSent = entry.DateSent,
        NotificationId = entry.NotificationId,
        Kind = entry.Kind?.ToString(),
        OrderId = entry.OrderId
    };
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public System.Collections.Generic.List<ReconciliationEntryDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationEntryDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? Kind { get; set; }
    public int? OrderId { get; set; }
}

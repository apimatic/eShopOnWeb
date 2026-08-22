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

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ReconciliationRowDto
{
    public string? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public ReconciliationRowDto[] Matched { get; set; } = Array.Empty<ReconciliationRowDto>();
    public ReconciliationRowDto[] ProviderOnly { get; set; } = Array.Empty<ReconciliationRowDto>();
    public ReconciliationRowDto[] EshopOnly { get; set; } = Array.Empty<ReconciliationRowDto>();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notifications);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
    {
        if (!DateTimeOffset.TryParse(request.From, out var fromUtc) || !DateTimeOffset.TryParse(request.To, out var toUtc))
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });

        try
        {
            var report = await notifications.ReconcileAsync(fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(),
                _httpContextAccessor.HttpContext?.RequestAborted ?? default);

            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Truncated = report.Truncated,
                Matched = report.Matched.Select(ToDto).ToArray(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToArray(),
                EshopOnly = report.EshopOnly.Select(ToDto).ToArray()
            });
        }
        catch (Exception ex)
        {
            return ex.ToHttpResult();
        }
    }

    private static ReconciliationRowDto ToDto(ReconciliationRow row) => new()
    {
        NotificationId = row.EshopNotificationId,
        ProviderSid = row.ProviderSid,
        Status = row.Status,
        Source = row.Source
    };
}

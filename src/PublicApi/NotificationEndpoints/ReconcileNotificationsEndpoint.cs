using System;
using System.Collections.Generic;
using System.Globalization;
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

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationItemDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? ProviderDateCreated { get; set; }
    public DateTimeOffset? LocalCreatedAt { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> LocalOnly { get; set; } = new();
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationService notifications) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue) ||
                    !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
                {
                    return Results.BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });
                }

                return await HandleAsync(new ReconciliationRequest(fromValue, toValue), notifications);
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
                Matched = report.Matched.Select(ToDto).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                LocalOnly = report.LocalOnly.Select(ToDto).ToList()
            });
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static ReconciliationItemDto ToDto(NotificationReconciliationItem item) =>
        new()
        {
            ProviderMessageSid = item.ProviderMessageSid,
            NotificationId = item.NotificationId,
            Source = item.Source,
            ProviderStatus = item.ProviderStatus,
            LocalStatus = item.LocalStatus,
            ProviderDateCreated = item.ProviderDateCreated,
            LocalCreatedAt = item.LocalCreatedAt
        };
}

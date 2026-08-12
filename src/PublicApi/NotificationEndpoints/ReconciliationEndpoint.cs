using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }

    /// <summary>Messages present in both records.</summary>
    public List<ReconciledMessage> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ReconciledMessage> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not contain.</summary>
    public List<ReconciledMessage> EShopOnly { get; set; } = new();
}

/// <summary>
/// Reconciles the provider's own record of messages from this application's configured sending number
/// against what eShop believes it sent, over an ISO-8601 date-time range (operator action).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                if (!TryParseIso(from, out var fromValue) || !TryParseIso(to, out var toValue))
                    return Results.BadRequest(new { error = "'from' and 'to' must be ISO-8601 date-times." });

                if (toValue < fromValue)
                    return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });

                return await HandleAsync(new ReconciliationRequest { From = fromValue, To = toValue }, service);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            EShopOnly = report.EShopOnly
        };
        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
    }
}

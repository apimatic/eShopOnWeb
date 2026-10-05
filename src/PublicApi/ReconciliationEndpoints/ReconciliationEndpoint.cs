using System;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

/// <summary>
/// Operator report: PayPal's own transaction record for [from, to] (every page of every 31-day window) lined up
/// against eShop orders, listing what only PayPal knows and what only eShop knows.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, ReconciliationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest
                {
                    From = ParseIso(from, nameof(from)),
                    To = ParseIso(to, nameof(to)),
                    RequestAborted = ct
                }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ReconciliationService service)
    {
        var report = await service.BuildAsync(request.From, request.To, request.RequestAborted);
        return Results.Ok(report);
    }

    private static DateTimeOffset ParseIso(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new PaymentRequestException(PaymentErrorKind.Validation, "invalid_date",
                $"'{name}' is required and must be an ISO-8601 date-time, e.g. 2026-10-01T00:00:00Z.");
        }
        return parsed;
    }
}

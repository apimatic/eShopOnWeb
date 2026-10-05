using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public ReconciliationReport? Report { get; set; }
}

/// <summary>
/// Operator report: PayPal's own transaction record for a date range lined up against eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, ReconciliationService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to, CancellationToken = cancellationToken }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ReconciliationService service)
    {
        var from = ParseIso8601(request.From, "from");
        var to = ParseIso8601(request.To, "to");
        var report = await service.BuildAsync(from, to, request.CancellationToken);
        return Results.Ok(new ReconciliationResponse(request.CorrelationId()) { Report = report });
    }

    private static DateTimeOffset ParseIso8601(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            throw new PaymentValidationException($"Query parameter '{name}' must be an ISO-8601 date-time, e.g. 2026-10-01T00:00:00Z.");
        return parsed;
    }
}

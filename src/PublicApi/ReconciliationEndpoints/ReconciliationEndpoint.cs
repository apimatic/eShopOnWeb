using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    /// <summary>Range start, ISO-8601 date-time.</summary>
    public DateTimeOffset From { get; set; }

    /// <summary>Range end, ISO-8601 date-time.</summary>
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationReport Report { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders, so a transaction only one side knows about
/// is visible. Covers the whole range, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPaymentService _paymentService;

    public ReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.From == default || request.To == default)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' query parameters (ISO-8601 date-times) are required." });
        }

        try
        {
            var report = await _paymentService.GetReconciliationReportAsync(request.From, request.To);
            return Results.Ok(new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                Report = report
            });
        }
        catch (Exception ex) when (PaymentEndpointHelpers.TryMapException(ex) is { } result)
        {
            return result;
        }
    }
}

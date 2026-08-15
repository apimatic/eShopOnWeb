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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to) { From = from; To = to; }
}

public class ReconciliationRowDto
{
    public string State { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? EShopReference { get; set; }
    public string? EShopPaymentStatus { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

/// <summary>
/// Operator report (admin only): lists PayPal's own transactions for a date range and lines them up
/// against eShop payments over the WHOLE range, so a payment one side has and the other lacks shows.
/// <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService reconciliationService) =>
            {
                if (!TryParseIso(from, out var fromValue) || !TryParseIso(to, out var toValue))
                    return Results.BadRequest("'from' and 'to' must be ISO-8601 date-times.");
                return await HandleAsync(new ReconciliationRequest(fromValue, toValue), reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var response = new ReconciliationResponse(request.CorrelationId());
        var report = await reconciliationService.ReconcileAsync(request.From, request.To);

        response.From = report.From;
        response.To = report.To;
        response.MatchedCount = report.MatchedCount;
        response.PayPalOnlyCount = report.PayPalOnlyCount;
        response.EShopOnlyCount = report.EShopOnlyCount;
        response.Rows = report.Rows.Select(r => new ReconciliationRowDto
        {
            State = r.State.ToString(),
            OrderId = r.OrderId,
            EShopReference = r.EShopReference,
            EShopPaymentStatus = r.EShopPaymentStatus,
            EShopAmount = r.EShopAmount,
            PayPalTransactionId = r.PayPalTransactionId,
            PayPalStatus = r.PayPalStatus,
            PayPalAmount = r.PayPalAmount,
            Currency = r.CurrencyCode,
            PayPalDate = r.PayPalDate
        }).ToList();

        return Results.Ok(response);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out result);
    }
}

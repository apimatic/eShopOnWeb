using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Compares Finix's transaction records with eShop orders for a date range.
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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                var start = from ?? DateTimeOffset.UtcNow.AddDays(-30);
                var end = to ?? DateTimeOffset.UtcNow;
                return await HandleAsync(new ReconciliationRequest(start, end));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var report = await _paymentService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FinixTransactionCount = report.FinixTransactionCount,
            EshopPaymentCount = report.EshopPaymentCount
        };

        response.FinixTransactions.AddRange(report.FinixTransactions.Select(PaymentDtoMapper.ToDto));
        response.UnmatchedFinixTransactions.AddRange(report.UnmatchedFinixTransactions.Select(PaymentDtoMapper.ToDto));
        response.UnmatchedEshopPayments.AddRange(report.UnmatchedEshopPayments.Select(PaymentDtoMapper.ToDto));

        return Results.Ok(response);
    }
}

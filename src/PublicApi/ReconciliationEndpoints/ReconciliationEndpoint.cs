using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range
/// (ISO-8601 date-times) lined up against eShop orders, covering the whole
/// range — both transactions eShop doesn't know and eShop payments PayPal's
/// report doesn't show.
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
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request) => HandleAsync(request, CancellationToken.None);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, CancellationToken ct)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new { message = "'to' must be after 'from'." });
        }

        try
        {
            var report = await _paymentService.ReconcileAsync(request.From, request.To, ct);

            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                Entries = report.Entries.Select(e => new ReconciliationEntryDto
                {
                    PayPalTransactionId = e.PayPalTransactionId,
                    PayPalReferenceId = e.PayPalReferenceId,
                    PayPalStatus = e.PayPalStatus,
                    Amount = e.Amount,
                    Currency = e.Currency,
                    Fee = e.Fee,
                    TransactionTime = e.TransactionTime,
                    OrderId = e.OrderId,
                    MatchStatus = e.MatchStatus
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentErrorMapper.ToErrorResult(ex);
        }
    }
}

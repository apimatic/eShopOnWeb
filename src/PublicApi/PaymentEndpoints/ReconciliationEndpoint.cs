using System;
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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator report that lists Finix's transactions for a date range and matches them to eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (IPaymentService paymentService) =>
                {
                    return await HandleAsync(paymentService);
                })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService)
    {
        var query = _httpContextAccessor.HttpContext?.Request.Query;
        var from = query?["from"].ToString();
        var to = query?["to"].ToString();

        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue))
        {
            throw new PaymentValidationException("from must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
        {
            throw new PaymentValidationException("to must be an ISO-8601 date-time.");
        }

        var report = await paymentService.ReconcileAsync(fromValue, toValue,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(ToResponse(report));
    }

    private static ReconciliationResponse ToResponse(ReconciliationReport report)
    {
        return new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FinixTransactions = report.FinixTransactions.Select(t => new ReconciliationFinixTransactionResponse
            {
                Id = t.Id,
                Kind = t.Kind,
                Type = t.Type,
                State = t.State,
                AmountCents = t.AmountCents,
                FeeCents = t.FeeCents,
                Currency = t.Currency,
                CreatedAt = t.CreatedAt,
                ParentTransferId = t.ParentTransferId,
                OrderId = t.OrderId,
                Matched = t.Matched
            }).ToList(),
            EShopOrders = report.EShopOrders.Select(o => new ReconciliationOrderResponse
            {
                OrderId = o.OrderId,
                OrderDate = o.OrderDate,
                AmountCents = o.AmountCents,
                Currency = o.Currency,
                PaymentStatus = o.PaymentStatus,
                FinixAuthorizationId = o.FinixAuthorizationId,
                FinixCaptureTransferId = o.FinixCaptureTransferId,
                FinixRefundTransferIds = o.FinixRefundTransferIds.ToList(),
                Matched = o.Matched
            }).ToList(),
            UnmatchedFinixTransactionIds = report.UnmatchedFinixTransactionIds.ToList(),
            UnmatchedOrderIds = report.UnmatchedOrderIds.ToList()
        };
    }
}

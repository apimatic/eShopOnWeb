using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: reconciles Finix transactions in a date range against eShop payments/refunds.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public ReconciliationEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to },
                    user, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (request.From is null || request.To is null)
        {
            throw new PaymentValidationException(
                "Both 'from' and 'to' ISO-8601 date-times are required.");
        }

        var report = await _orderPaymentService.ReconcileAsync(
            request.From.Value, request.To.Value, cancellationToken);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToResponse).ToList(),
            FinixOnly = report.FinixOnly.Select(ToResponse).ToList(),
            EshopOnly = report.EshopOnly.Select(ToResponse).ToList(),
            FinixTransactionCount = report.FinixTransactionCount,
            EshopTransactionCount = report.EshopTransactionCount
        };

        return Results.Ok(response);
    }

    private static ReconciliationItemResponse ToResponse(ReconciliationItem item) =>
        new()
        {
            FinixTransactionId = item.FinixTransactionId,
            OrderId = item.OrderId,
            RefundId = item.RefundId,
            Kind = item.Kind,
            AmountMinor = item.AmountMinor,
            Amount = item.Amount,
            Currency = item.Currency,
            State = item.State,
            CreatedAt = item.CreatedAt,
            OrderReference = item.OrderReference,
            Note = item.Note
        };
}

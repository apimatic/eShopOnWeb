using System;
using System.Collections.Generic;
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
/// Operator: lists PayPal's own record of transactions over a date range (all pages) and
/// lines them up against eShop orders, surfacing entries known only to one side.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentService, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from) ||
            !DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new PaymentConflictException("Query parameters 'from' and 'to' are required and must be ISO-8601 date-times.");
        }

        var report = await paymentService.ReconcileAsync(from, to, cancellationToken);

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Transactions = report.Transactions,
            PaymentsMissingFromPayPal = report.PaymentsMissingFromPayPal
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<ReconciliationEntry> Transactions { get; set; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationUnmatchedPayment> PaymentsMissingFromPayPal { get; set; } = Array.Empty<ReconciliationUnmatchedPayment>();
}

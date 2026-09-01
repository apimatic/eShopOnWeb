using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: PayPal's own record of transactions over [from, to] (ISO-8601 date-times), lined up
/// against eShop orders — covering the whole range, not just the first page. Transactions PayPal
/// knows about but eShop doesn't come back with a null matchedOrderId; captures eShop knows about
/// but PayPal's report doesn't (yet — sandbox reporting lags) come back in missingFromPayPal.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public GetReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, user);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ClaimsPrincipal user)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await _paymentService.ReconcileAsync(request.From, request.To, CancellationToken.None);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Currency = report.Currency,
            Transactions = report.Transactions,
            UnmatchedTransactions = report.UnmatchedTransactions,
            MissingFromPayPal = report.MissingFromPayPal
        };
        return Results.Ok(response);
    }
}

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
    public string Currency { get; set; } = string.Empty;
    public IReadOnlyList<ReconciliationTransaction> Transactions { get; set; } = Array.Empty<ReconciliationTransaction>();
    public IReadOnlyList<ReconciliationTransaction> UnmatchedTransactions { get; set; } = Array.Empty<ReconciliationTransaction>();
    public IReadOnlyList<ReconciliationLocalPayment> MissingFromPayPal { get; set; } = Array.Empty<ReconciliationLocalPayment>();
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationRequest : BaseRequest
{
    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class GetReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();
    public List<UnmatchedOrder> OrdersWithoutPayPalTransaction { get; set; } = new List<UnmatchedOrder>();
}

/// <summary>
/// Operator report: PayPal's own record of transactions over a date range (all pages), lined
/// up against eShop orders so a mismatch in either direction is visible.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest>
{
    private readonly OrderPaymentService _paymentService;

    public GetReconciliationEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            {
                return await Handle(new GetReconciliationRequest(from, to), ct);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(GetReconciliationRequest request)
        => Handle(request, CancellationToken.None);

    private async Task<IResult> Handle(GetReconciliationRequest request, CancellationToken ct)
    {
        try
        {
            if (request.To <= request.From)
            {
                return Results.BadRequest(new { message = "The 'to' date-time must be after 'from'." });
            }

            var report = await _paymentService.ReconcileAsync(request.From, request.To, ct);
            return Results.Ok(new GetReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Transactions = new List<ReconciliationEntry>(report.Transactions),
                OrdersWithoutPayPalTransaction = new List<UnmatchedOrder>(report.OrdersWithoutPayPalTransaction)
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}

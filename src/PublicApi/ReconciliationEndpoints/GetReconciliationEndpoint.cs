using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: lists PayPal's own record of transactions over a date range (all pages),
/// lined up against eShop orders and payments. from/to are ISO-8601 date-times.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), paymentService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IPaymentService paymentService)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from) ||
            !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var to))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await paymentService.GetReconciliationAsync(from, to);

        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            TotalPayPalTransactions = report.TotalPayPalTransactions,
            MatchedCount = report.MatchedCount,
            UnmatchedPayPalCount = report.UnmatchedPayPalCount,
            Transactions = report.Transactions,
            PaymentsMissingFromPayPal = report.PaymentsMissingFromPayPal
        };

        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public string From { get; }
    public string To { get; }

    public GetReconciliationRequest(string from, string to)
    {
        From = from;
        To = to;
    }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedPayPalCount { get; set; }
    public System.Collections.Generic.List<ReconciliationEntryDto> Transactions { get; set; } = new();
    public System.Collections.Generic.List<UnmatchedPaymentDto> PaymentsMissingFromPayPal { get; set; } = new();
}

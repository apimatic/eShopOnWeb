using System;
using System.Globalization;
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

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationResult Report { get; set; } = new ReconciliationResult();
}

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up against
/// eShop orders. Entries PayPal knows about but eShop does not are marked "paypal-only";
/// eShop payments absent from PayPal's report are listed under localPaymentsNotInPayPal.
/// Covers the whole range, not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, string, string>
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
            (string from, string to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDate)
            || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDate))
        {
            throw new PaymentException(400, "Parameters 'from' and 'to' are required and must be ISO-8601 date-times.");
        }

        if (toDate <= fromDate)
        {
            throw new PaymentException(400, "Parameter 'to' must be later than 'from'.");
        }

        var response = new GetReconciliationResponse
        {
            From = fromDate,
            To = toDate,
            Report = await _paymentService.ReconcileAsync(fromDate, toDate)
        };

        return Results.Ok(response);
    }
}

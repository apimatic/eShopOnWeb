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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

/// <summary>
/// Operator action: reconcile PayPal's own transaction records against eShop orders for a date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public ReconciliationEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string? from, string? to, IPaymentService service) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, service))
            .Produces<ReconciliationReport>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService service)
    {
        if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
            throw new PaymentValidationException("'from' and 'to' must be ISO-8601 date-times.");
        if (to < from)
            throw new PaymentValidationException("'to' must not be before 'from'.");

        var report = await service.ReconcileAsync(from, to, _http.HttpContext!.RequestAborted);
        return Results.Ok(report);
    }

    private static bool TryParse(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

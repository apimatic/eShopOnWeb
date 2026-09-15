using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest : BaseRequest
{
    [JsonIgnore]
    public DateTimeOffset From { get; set; }
    [JsonIgnore]
    public DateTimeOffset To { get; set; }
}

/// <summary>
/// Operator action: lists PayPal's own transactions for a date range and lines them up against
/// eShop orders, surfacing payments PayPal knows about that eShop doesn't (and the reverse).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderPaymentService service) =>
            {
                if (!TryParseIso(from, out var fromValue) || !TryParseIso(to, out var toValue))
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' are required and must be ISO-8601 date-times." });
                }
                return await HandleAsync(new ReconciliationRequest { From = fromValue, To = toValue }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out result);
    }
}

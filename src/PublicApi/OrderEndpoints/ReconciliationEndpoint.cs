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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService paymentService)
    {
        try
        {
            if (!TryParseTimestamp(request.From, out var from) || !TryParseTimestamp(request.To, out var to))
            {
                throw new PaymentException("`from` and `to` must be ISO-8601 date-times, for example 2026-01-01T00:00:00Z.");
            }

            var report = await paymentService.ReconcileAsync(from, to);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Matches = report.Matches,
                PayPalOnly = report.PayPalOnly,
                EshopOnly = report.EshopOnly
            });
        }
        catch (Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(value + "T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
        {
            return true;
        }

        return false;
    }
}

public record ReconciliationRequest(string From, string To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public object Matches { get; set; } = new();
    public object PayPalOnly { get; set; } = new();
    public object EshopOnly { get; set; } = new();
}

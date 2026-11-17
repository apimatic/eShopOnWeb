using System;
using System.Globalization;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: lists Finix's record of transactions in a date range and
/// lines them up against eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(
                Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var fromValue = ParseDate(from, "from");
                var toValue = ParseDate(to, "to");

                var result = await paymentService.GetReconciliationAsync(fromValue, toValue, cancellationToken);
                return Results.Ok(result);
            })
            .Produces<ApplicationCore.Entities.PaymentAggregate.ReconciliationResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("OrderPaymentEndpoints");
    }

    private static DateTimeOffset ParseDate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentValidationException($"Query parameter '{parameterName}' is required.");
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new PaymentValidationException(
            $"Query parameter '{parameterName}' must be an ISO-8601 date-time.");
    }
}

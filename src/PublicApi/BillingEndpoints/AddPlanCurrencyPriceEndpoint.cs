using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class AddCurrencyPriceRequest
{
    /// <summary>ISO 4217 currency code, e.g. EUR.</summary>
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Prices a plan in another currency
/// </summary>
public class AddPlanCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, AddCurrencyPriceRequest request, IBillingService billing, CancellationToken ct) =>
            {
                var currency = request?.Currency?.Trim().ToUpperInvariant();
                if (currency is null || currency.Length != 3 || !System.Linq.Enumerable.All(currency, char.IsAsciiLetterUpper))
                    throw BillingException.BadRequest("currency is required and must be a three-letter currency code.");
                if (request!.Price is null || request.Price <= 0)
                    throw BillingException.BadRequest("price is required and must be greater than zero.");
                var created = await billing.AddCurrencyPriceAsync(planId, currency, request.Price.Value, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices", created);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

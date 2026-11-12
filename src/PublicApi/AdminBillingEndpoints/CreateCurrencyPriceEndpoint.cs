using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminBillingEndpoints;

public class CreateCurrencyPriceRequest : BaseRequest
{
    /// <summary>ISO 4217 code, for example EUR.</summary>
    public string? Currency { get; set; }

    /// <summary>The plan's monthly price in that currency. Maxio takes whole currency units.</summary>
    public decimal? Price { get; set; }

    internal int PlanId { get; set; }
}

public class CurrencyPriceDto
{
    public string? Currency { get; set; }
    public double? Price { get; set; }
    public string? FormattedPrice { get; set; }
}

public class CreateCurrencyPriceResponse : BaseResponse
{
    public CreateCurrencyPriceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateCurrencyPriceResponse()
    {
    }

    public int PlanId { get; set; }
    public List<CurrencyPriceDto> CurrencyPrices { get; set; } = new();
}

/// <summary>
/// Prices a plan in another currency (staff only)
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint<IResult, CreateCurrencyPriceRequest, PlanCatalog>
{
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest request, PlanCatalog catalog, HttpContext http) =>
            {
                request.PlanId = planId;
                return await HandleAsync(request, catalog, http.RequestAborted);
            })
            .Produces<CreateCurrencyPriceResponse>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(CreateCurrencyPriceRequest request, PlanCatalog catalog)
        => HandleAsync(request, catalog, default);

    public async Task<IResult> HandleAsync(CreateCurrencyPriceRequest request, PlanCatalog catalog, System.Threading.CancellationToken ct)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || !CurrencyCode.IsMatch(currency))
            throw MaxioProblemException.BadRequest("currency is required and must be a three-letter ISO 4217 code such as EUR.");

        if (request.Price is null) throw MaxioProblemException.BadRequest("price is required.");
        var price = request.Price.Value;
        if (price < 0 || price > 1_000_000) throw MaxioProblemException.BadRequest("price must be between 0 and 1000000.");
        if (decimal.Truncate(price) != price)
            throw MaxioProblemException.BadRequest("price must be a whole number of currency units; Maxio does not accept fractions here.");

        var prices = await catalog.AddCurrencyPriceAsync(request.PlanId, currency, (int)price, ct);
        var response = new CreateCurrencyPriceResponse(request.CorrelationId())
        {
            PlanId = request.PlanId,
            CurrencyPrices = prices.Select(p => new CurrencyPriceDto { Currency = p.Currency, Price = p.Price, FormattedPrice = p.FormattedPrice }).ToList(),
        };
        return Results.Created($"api/admin/plans/{request.PlanId}/currency-prices", response);
    }
}

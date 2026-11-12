using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreatePlanCurrencyPriceRequest : BaseRequest
{
    /// <summary>ISO 4217 code, for example EUR.</summary>
    public string? Currency { get; set; }

    /// <summary>The price in that currency, in whole currency units.</summary>
    public decimal? Price { get; set; }
}

/// <summary>
/// Prices a plan in another currency
/// </summary>
public class CreatePlanCurrencyPriceEndpoint : IEndpoint<IResult, string, CreatePlanCurrencyPriceRequest>
{
    private readonly PlanService _plans;

    public CreatePlanCurrencyPriceEndpoint(PlanService plans)
    {
        _plans = plans;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (string planId, CreatePlanCurrencyPriceRequest request) =>
            {
                return await HandleAsync(planId, request);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(string planId, CreatePlanCurrencyPriceRequest request)
    {
        if (request.Price is null)
        {
            throw new BillingException(400, "The currency price needs a price.");
        }

        var created = await _plans.AddCurrencyPriceAsync(planId, request.Currency ?? string.Empty, request.Price.Value);
        return Results.Created($"api/admin/plans/{created.PlanId}/currency-prices", created);
    }
}

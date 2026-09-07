using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> maxioSettings)
    {
        _client = client;
        _maxioSettings = maxioSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var products = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: CancellationToken.None);

            response.Plans.AddRange(products
                .Where(p => p.Product != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product.Id ?? 0,
                    Name = p.Product.Name,
                    Handle = p.Product.Handle,
                    Description = p.Product.Description,
                    PriceInCents = p.Product.PriceInCents,
                    Interval = p.Product.Interval,
                    IntervalUnit = p.Product.IntervalUnit?.Value,
                    TrialPriceInCents = p.Product.TrialPriceInCents,
                    TrialInterval = p.Product.TrialInterval,
                    TrialIntervalUnit = p.Product.TrialIntervalUnit?.Value
                }));

            return Results.Ok(response);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }

    public class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}

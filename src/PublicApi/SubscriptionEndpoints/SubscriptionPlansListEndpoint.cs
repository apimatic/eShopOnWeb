using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;

    public SubscriptionPlansListEndpoint(MaxioAdvancedBillingClient client, IConfiguration configuration)
    {
        _client = client;
        _configuration = configuration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (CancellationToken ct) => await HandleAsync(ct))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    async Task<IResult> IEndpoint<IResult>.HandleAsync()
    {
        return Results.BadRequest();
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var familyHandle = _configuration["Maxio:ProductFamilyHandle"];
            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: cancellationToken);

            var filtered = response
                .Where(p => p.Product?.ProductFamily?.Handle == familyHandle)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product?.Id,
                    Name = p.Product?.Name,
                    Handle = p.Product?.Handle,
                    PriceInCents = p.Product?.PriceInCents,
                    Interval = p.Product?.Interval,
                    IntervalUnit = p.Product?.IntervalUnit?.ToString()
                })
                .ToList();

            return Results.Ok(new ListSubscriptionPlansResponse { Plans = filtered });
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = [];
}

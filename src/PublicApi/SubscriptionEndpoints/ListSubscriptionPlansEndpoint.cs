using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(IMaxioApiClient maxioClient, ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync()
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            var products = await _maxioClient.GetProductsByFamilyHandleAsync("eshop-subscribe");
            _logger.LogInformation("Retrieved {Count} subscription plans from Maxio", products.Length);

            foreach (var product in products)
            {
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name,
                    Description = product.Description ?? string.Empty,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list subscription plans");
            return Results.BadRequest(new { error = "Failed to list subscription plans" });
        }
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

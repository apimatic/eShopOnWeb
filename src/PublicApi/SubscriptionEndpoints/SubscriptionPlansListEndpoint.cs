using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for the configured Maxio Product Family.
/// GET /api/subscription-plans
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly MaxioSettings _maxioSettings;

    public SubscriptionPlansListEndpoint(IMaxioSubscriptionService subscriptionService, IOptions<MaxioSettings> maxioSettings)
    {
        _subscriptionService = subscriptionService;
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

        var plans = await _subscriptionService.GetAvailablePlansAsync(_maxioSettings.ProductFamilyHandle ?? string.Empty);
        response.Plans.AddRange(plans.Select(SubscriptionPlanDto.From));

        return Results.Ok(response);
    }
}

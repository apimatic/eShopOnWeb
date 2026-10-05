using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to the signed-in shopper.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly IMaxioBillingService _billingService;

    public SubscriptionPlansListEndpoint(IMaxioBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(401)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _billingService.ListPlansAsync(default);
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioEndpointResults.Failure(ex);
        }
    }
}
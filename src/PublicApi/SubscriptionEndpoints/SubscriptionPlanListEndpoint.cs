using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers (GET /api/subscription-plans).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetAvailablePlansAsync(CancellationToken.None);

            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(plans.Select(SubscriptionDtoMapping.ToPlanDto));
            return Results.Ok(response);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.NotConfigured(ex);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.MaxioFailure(ex);
        }
        catch (HttpRequestException ex)
        {
            return SubscriptionEndpointErrors.MaxioUnreachable(ex);
        }
        catch (OperationCanceledException)
        {
            return SubscriptionEndpointErrors.MaxioTimeout();
        }
    }
}

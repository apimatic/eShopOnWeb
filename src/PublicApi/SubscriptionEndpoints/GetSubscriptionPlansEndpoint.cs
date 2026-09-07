using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Get available subscription plans",
                Description = "Returns a list of available subscription plans",
                OperationId = "subscriptions.getPlans",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (SubscriptionService service) =>
            {
                return await HandleAsync(new EmptyRequest(), service);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, SubscriptionService service)
    {
        var response = new GetSubscriptionPlansResponse();

        try
        {
            var plans = await service.GetAvailablePlansAsync();
            response.Plans = plans;
            return Results.Ok(response);
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class EmptyRequest : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionPlanDto>? Plans { get; set; }
}

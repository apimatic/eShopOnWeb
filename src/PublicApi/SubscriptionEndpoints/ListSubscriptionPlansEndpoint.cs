using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for enrollment, with live prices from Maxio.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var result = await subscriptionService.ListPlansAsync();
        if (result.Status == Ardalis.Result.ResultStatus.Error)
        {
            response.Errors.AddRange(result.ValidationErrors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        foreach (var plan in result.Value)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                DisplayName = plan.DisplayName,
                PriceInCents = plan.PriceInCents,
                IsDefault = plan.IsDefault,
                Available = plan.Available,
                ProductFamilyHandle = plan.ProductFamilyHandle
            });
        }

        return Results.Ok(response);
    }
}
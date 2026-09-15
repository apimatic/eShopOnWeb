using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, EmptySubscriptionRequest>
{
    private readonly SubscriptionService _service;

    public SubscriptionPlanListEndpoint(SubscriptionService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", () => HandleAsync(new EmptySubscriptionRequest()))
            .Produces<SubscriptionPlansResponse>()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptySubscriptionRequest request)
    {
        try
        {
            return Results.Ok(new SubscriptionPlansResponse(await _service.ListPlansAsync(CancellationToken.None)));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing provider could not return subscription plans.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

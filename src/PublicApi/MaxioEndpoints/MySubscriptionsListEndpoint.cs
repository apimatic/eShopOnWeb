using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public class MySubscriptionsListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService maxioService)
    {
        var userReference = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userReference))
            return Results.Unauthorized();

        var subscriptions = await maxioService.ListMySubscriptionsAsync(userReference);
        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions
        };
        return Results.Ok(response);
    }

    public class ListMySubscriptionsResponse
    {
        public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
    }
}

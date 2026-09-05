using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's Maxio subscriptions
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public MySubscriptionsListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var request = new ListMySubscriptionsRequest(user.Identity?.Name ?? string.Empty);
                return await HandleAsync(request, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.ListCustomerSubscriptionsAsync(request.Username);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<CustomerSubscriptionDto>));

        return Results.Ok(response);
    }
}

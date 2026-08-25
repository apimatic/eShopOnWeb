using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMapper _mapper;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IMapper mapper)
    {
        _userManager = userManager;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? httpContext.User.Identity?.Name;
                if (username is null)
                {
                    return Results.Unauthorized();
                }

                var user = await _userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest { UserId = user.Id }, billingService);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var subscriptions = await billingService.ListMySubscriptionsAsync(request.UserId!);
            response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));
            return Results.Ok(response);
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}

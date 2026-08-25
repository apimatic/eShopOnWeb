using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions (plan, price, state, next billing date).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MaxioClient, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioClient maxioClient, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(maxioClient, userManager);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        var response = new ListMySubscriptionsResponse();

        var user = await CreateSubscriptionEndpoint.ResolveUserAsync(userManager, _httpContextAccessor.HttpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var customer = await maxioClient.FindCustomerByReferenceAsync(user.Id);
            if (customer is null)
            {
                return Results.Ok(response);
            }

            var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
            response.Subscriptions.AddRange(subscriptions
                .OrderByDescending(s => s.CreatedAt)
                .Select(SubscriptionMapper.ToDto));
            return Results.Ok(response);
        }
        catch (MaxioConfigurationException ex)
        {
            return Results.Problem(ex.Message, statusCode: (int)HttpStatusCode.ServiceUnavailable);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem($"Maxio billing error: {ex.ResponseBody}", statusCode: (int)HttpStatusCode.BadGateway);
        }
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioApiClient, MaxioOptions>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioApiClient maxioClient, MaxioOptions options) =>
            {
                return await HandleAsync(maxioClient, options);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        IMaxioApiClient maxioClient,
        MaxioOptions options)
    {
        var response = new ListMySubscriptionsResponse();

        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var email = user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("email")
            ?? user.Identity?.Name
            ?? throw new InvalidOperationException("Unable to determine user identity from JWT.");

        var customerReference = email;

        var customerId = await maxioClient.FindCustomerByReferenceAsync(customerReference);
        if (!customerId.HasValue)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.GetSubscriptionsForCustomerAsync(customerId.Value);
        response.Subscriptions.AddRange(subscriptions);

        return Results.Ok(response);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Get current user's subscriptions",
                Description = "Lists all active subscriptions for the current user",
                OperationId = "subscriptions.list-mine",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (HttpContext httpContext,
                MaxioClient maxioClient,
                UserManager<ApplicationUser> userManager,
                IReadRepository<UserSubscription> subscriptionRepository) =>
            {
                return await HandleAsync(httpContext, maxioClient, userManager, subscriptionRepository);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext,
        MaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IReadRepository<UserSubscription> subscriptionRepository)
    {
        var response = new ListMySubscriptionsResponse();

        try
        {
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var userSubscriptions = await subscriptionRepository.ListAsync(
                new UserSubscriptionsSpecification(user.Id));

            foreach (var userSub in userSubscriptions)
            {
                var maxioSubscriptions = await maxioClient.GetCustomerSubscriptionsAsync(userSub.MaxioCustomerId);
                var activeSub = maxioSubscriptions.FirstOrDefault(s => s.Id == userSub.MaxioSubscriptionId);

                if (activeSub != null)
                {
                    response.Subscriptions.Add(new UserSubscriptionDto
                    {
                        Id = activeSub.Id,
                        ProductHandle = activeSub.ProductHandle,
                        ProductName = activeSub.Product.Name,
                        Price = activeSub.Product.Price,
                        State = activeSub.State,
                        NextBillingAt = activeSub.NextBillingAt,
                        CreatedAt = activeSub.CreatedAt
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioApiClient maxioClient, UserManager<ApplicationUser> userManager, IRepository<ApplicationCore.Entities.MaxioCustomer> maxioCustomerRepository, HttpContext httpContext) =>
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

                    var maxioCustomers = await maxioCustomerRepository.ListAsync();
                    var maxioCustomerMapping = maxioCustomers.FirstOrDefault(m => m.ApplicationUserId == user.Id);

                    if (maxioCustomerMapping == null)
                    {
                        return Results.Ok(response);
                    }

                    var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(maxioCustomerMapping.MaxioId);

                    response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
                    {
                        Id = s.Id,
                        State = s.State,
                        CustomerId = s.CustomerId,
                        ProductId = s.Product.Id,
                        ProductName = s.Product.Name,
                        ProductHandle = s.Product.Handle,
                        ProductPriceInCents = s.Product.PriceInCents,
                        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                        NextAssessmentAt = s.NextAssessmentAt,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt
                    }));

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("ListMySubscriptions");
    }

    public Task<IResult> HandleAsync(IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException();
    }
}

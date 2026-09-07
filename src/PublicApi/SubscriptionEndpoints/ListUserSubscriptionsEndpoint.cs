using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMapper _mapper;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListUserSubscriptionsEndpoint(IMapper mapper, UserManager<ApplicationUser> userManager)
    {
        _mapper = mapper;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new EmptyRequest(), httpContext, billingService);
            })
            .WithName("GetMySubscriptions")
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("Subscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, HttpContext httpContext, IMaxioBillingService billingService)
    {
        var response = new ListUserSubscriptionsResponse();

        try
        {
            var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var subscriptions = await billingService.GetUserSubscriptionsAsync(userId);
            response.Subscriptions.AddRange(_mapper.Map<List<SubscriptionDto>>(subscriptions));

            return Results.Ok(response);
        }
        catch
        {
            return Results.Problem("Error retrieving subscriptions", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

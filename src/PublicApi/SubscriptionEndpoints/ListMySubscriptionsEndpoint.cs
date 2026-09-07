using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the current user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    private readonly IRepository<MaxioCustomer> _customerRepo;

    public ListMySubscriptionsEndpoint(IRepository<MaxioCustomer> customerRepo)
    {
        _customerRepo = customerRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext context, IMaxioService maxioService) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioService, context);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService, HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var spec = new MaxioCustomerByUserIdSpecification(userId);
        var customer = await _customerRepo.FirstOrDefaultAsync(spec);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.MaxioCustomerId);

        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.ProductHandle ?? "",
            CurrentPrice = s.CurrentPrice,
            NextBillingAt = s.NextBillingAt,
            BillingPeriodLength = s.BillingPeriodLength,
            BillingPeriodUnit = s.BillingPeriodUnit
        }).ToList();

        return Results.Ok(response);
    }
}

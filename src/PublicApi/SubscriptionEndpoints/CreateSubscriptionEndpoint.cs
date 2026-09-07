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
/// Create a subscription for the current user
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    private readonly IRepository<MaxioCustomer> _customerRepo;
    private readonly IRepository<Subscription> _subscriptionRepo;

    public CreateSubscriptionEndpoint(IRepository<MaxioCustomer> customerRepo, IRepository<Subscription> subscriptionRepo)
    {
        _customerRepo = customerRepo;
        _subscriptionRepo = subscriptionRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, HttpContext context, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, maxioService, context);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var spec = new MaxioCustomerByUserIdSpecification(userId);
        var existingCustomer = await _customerRepo.FirstOrDefaultAsync(spec);
        long customerId;

        if (existingCustomer == null)
        {
            var maxioCustomer = await maxioService.EnsureCustomerAsync(userId, email);
            customerId = maxioCustomer.Id;

            var newCustomer = new MaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _customerRepo.AddAsync(newCustomer);
        }
        else
        {
            customerId = existingCustomer.MaxioCustomerId;
        }

        var subscription = await maxioService.CreateSubscriptionAsync(customerId, request.ProductHandle);

        var dbSubscription = new Subscription
        {
            UserId = userId,
            MaxioSubscriptionId = subscription.Id,
            ProductHandle = subscription.ProductHandle ?? request.ProductHandle,
            State = subscription.State,
            CurrentPrice = subscription.CurrentPrice,
            NextBillingAt = subscription.NextBillingAt,
            BillingPeriodUnit = subscription.BillingPeriodUnit,
            BillingPeriodLength = subscription.BillingPeriodLength,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };
        await _subscriptionRepo.AddAsync(dbSubscription);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle ?? request.ProductHandle,
            CurrentPrice = subscription.CurrentPrice,
            NextBillingAt = subscription.NextBillingAt,
            BillingPeriodLength = subscription.BillingPeriodLength,
            BillingPeriodUnit = subscription.BillingPeriodUnit
        };

        return Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }
}

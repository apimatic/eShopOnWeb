using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService,
                IRepository<UserSubscriptionMapping> mappingRepository, IRepository<Subscription> subscriptionRepository) =>
            {
                return await HandleAsync(request, maxioService, httpContext, mappingRepository, subscriptionRepository);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService,
        HttpContext httpContext, IRepository<UserSubscriptionMapping> mappingRepository,
        IRepository<Subscription> subscriptionRepository)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.BadRequest(response);

        try
        {
            var spec = new UserSubscriptionMappingByUserIdSpecification(userId);
            var userMapping = await mappingRepository.FirstOrDefaultAsync(spec);

            int maxioCustomerId;
            if (userMapping != null)
            {
                maxioCustomerId = userMapping.MaxioCustomerId;
            }
            else
            {
                var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@example.com";
                var userNameParts = (httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "User").Split(' ');
                var firstName = userNameParts.Length > 0 ? userNameParts[0] : "User";
                var lastName = userNameParts.Length > 1 ? userNameParts[1] : "";

                var customer = await maxioService.GetOrCreateCustomerAsync(userId, firstName, lastName, userEmail);
                if (customer == null)
                {
                    response.ErrorMessage = "Failed to create Maxio customer";
                    return Results.BadRequest(response);
                }

                maxioCustomerId = customer.Id;

                var newMapping = new UserSubscriptionMapping(userId, maxioCustomerId);
                await mappingRepository.AddAsync(newMapping);
            }

            var maxioSubscription = await maxioService.CreateSubscriptionAsync(maxioCustomerId, request.ProductHandle);

            var subscription = new Subscription(
                userId,
                maxioCustomerId,
                maxioSubscription.Id,
                maxioSubscription.Product?.Handle ?? "unknown",
                maxioSubscription.State,
                maxioSubscription.Product?.PriceInCents / 100m ?? 0,
                maxioSubscription.NextAssessmentAt
            );

            await subscriptionRepository.AddAsync(subscription);

            response.Subscription = new SubscriptionDto
            {
                Id = maxioSubscription.Id,
                ProductName = maxioSubscription.Product?.Name ?? "",
                ProductHandle = maxioSubscription.Product?.Handle ?? "",
                Price = maxioSubscription.Product?.PriceInCents / 100m ?? 0,
                State = maxioSubscription.State,
                NextBillingDate = maxioSubscription.NextAssessmentAt
            };

            return Results.Created($"/api/subscriptions/{maxioSubscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}

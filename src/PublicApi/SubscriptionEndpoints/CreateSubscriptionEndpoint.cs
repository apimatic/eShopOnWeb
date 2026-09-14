using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// The call is idempotent: a second identical request (double click, client retry, replayed
/// idempotency key) returns the subscription created by the first one with
/// <c>alreadySubscribed = true</c> and 200 OK, rather than enrolling the shopper twice.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionApiService>
{
    private readonly IMapper _mapper;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IMapper mapper, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _mapper = mapper;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionApiService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptions, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionApiService subscriptions) =>
        HandleAsync(request, user, subscriptions, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        ISubscriptionApiService subscriptions,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors, extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = response.CorrelationId()
            });
        }

        return await SubscriptionProblems.ExecuteAsync(async () =>
        {
            var result = await subscriptions.SubscribeAsync(user, request.PlanHandle, request.IdempotencyKey, cancellationToken);

            response.Subscription = _mapper.Map<CustomerSubscriptionDto>(result.Subscription);
            response.AlreadySubscribed = result.AlreadySubscribed;

            _logger.LogInformation(
                "Subscribe request {CorrelationId} resolved to subscription {SubscriptionId} on plan {PlanHandle} (alreadySubscribed: {AlreadySubscribed}).",
                response.CorrelationId(),
                result.Subscription.Id,
                result.Subscription.PlanHandle,
                result.AlreadySubscribed);

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }, _logger, response.CorrelationId());
    }

    private static Dictionary<string, string[]> Validate(CreateSubscriptionRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty).Select(member => (member, result.ErrorMessage)))
            .GroupBy(entry => entry.member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ErrorMessage ?? "Invalid value.").ToArray());
    }
}

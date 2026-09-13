using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioClient _maxioClient;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Creates a new subscription",
        Description = "Subscribes the authenticated user to a plan, idempotently creating a Maxio customer if needed",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var email = User.FindFirstValue(ClaimTypes.Email) ?? $"{userId}@eshop.local";
        var firstName = User.FindFirstValue("given_name") ?? userId;
        var lastName = User.FindFirstValue("family_name") ?? "User";
        var customerRef = $"eshop-{userId}";

        var existingCustomer = await _maxioClient.LookupCustomerByReferenceAsync(customerRef, cancellationToken);
        int customerId;

        if (existingCustomer != null)
        {
            customerId = existingCustomer.Id;
        }
        else
        {
            var newCustomer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerRef,
                Organization = "eShopOnWeb"
            }, cancellationToken);
            customerId = newCustomer.Id;
        }

        // Create a test payment profile (sandbox bogus gateway)
        var paymentProfile = await _maxioClient.CreatePaymentProfileAsync(customerId, new MaxioCreatePaymentProfileRequest
        {
            FirstName = firstName,
            LastName = lastName,
            CardNumber = "1",
            ExpirationMonth = 12,
            ExpirationYear = 2030,
            BillingAddress = "123 Main St",
            BillingCity = "Boston",
            BillingState = "MA",
            BillingZip = "02101",
            BillingCountry = "US"
        }, cancellationToken);

        // Idempotency: check for existing active subscription to the same product
        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product?.Handle == request.ProductHandle &&
            s.State is "active" or "trialing");

        if (existing != null)
        {
            response.SubscriptionId = existing.Id;
            response.State = existing.State;
            response.MaxioCustomerId = customerId;
            response.PlanHandle = request.ProductHandle;
            response.PlanName = existing.Product?.Name ?? request.ProductHandle;
            response.PlanPrice = existing.Product?.Price ?? 0m;
            response.NextBillingDate = existing.NextAssessmentAt;
            response.Message = $"Already subscribed to {response.PlanName}.";
            return Ok(response);
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            ProductHandle = request.ProductHandle,
            CustomerId = customerId,
            PaymentProfileId = paymentProfile.Id
        }, cancellationToken);

        response.SubscriptionId = subscription.Id;
        response.State = subscription.State;
        response.MaxioCustomerId = customerId;
        response.PlanHandle = request.ProductHandle;
        response.PlanName = subscription.Product?.Name ?? request.ProductHandle;
        response.PlanPrice = subscription.Product?.Price ?? 0m;
        response.NextBillingDate = subscription.NextAssessmentAt;
        response.Message = $"Successfully subscribed to {response.PlanName}.";

        return Ok(response);
    }
}

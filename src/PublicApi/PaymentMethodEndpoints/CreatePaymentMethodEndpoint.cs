using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    private readonly PayPalService _payPal;

    public CreatePaymentMethodEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request,
                   IRepository<SavedPaymentMethod> repository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.Number))
                    return Results.BadRequest("Card number is required.");
                if (string.IsNullOrEmpty(request.Expiry))
                    return Results.BadRequest("Card expiry is required.");

                // Get existing PayPal customer ID if the buyer already has saved cards
                var existingSpec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var existingMethods = await repository.ListAsync(existingSpec, ct);
                var existingCustomerId = existingMethods.FirstOrDefault()?.PayPalCustomerId;

                var card = new CardDetails(
                    Number: request.Number,
                    Expiry: request.Expiry,
                    SecurityCode: request.SecurityCode,
                    Name: request.Name,
                    BillingCountryCode: request.BillingCountryCode);

                var result = await _payPal.SaveCardAsync(existingCustomerId, card, ct);

                var method = new SavedPaymentMethod(
                    buyerId: buyerId,
                    payPalCustomerId: result.PayPalCustomerId,
                    vaultTokenId: result.VaultTokenId,
                    lastDigits: result.LastDigits,
                    brand: result.Brand,
                    expiry: result.Expiry);

                method = await repository.AddAsync(method, ct);

                return Results.Created($"api/payment-methods/{method.Id}", new CreatePaymentMethodResponse
                {
                    PaymentMethodId = method.Id,
                    LastDigits = method.LastDigits,
                    Brand = method.Brand,
                    Expiry = method.Expiry
                });
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IRepository<SavedPaymentMethod> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse() : base(System.Guid.NewGuid()) { }
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? Expiry { get; set; }
}

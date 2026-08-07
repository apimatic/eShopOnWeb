using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper by vaulting it at PayPal and storing only PCI-safe
/// display data plus the vault token.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly ILogger<CreatePaymentMethodEndpoint> _logger;

    public CreatePaymentMethodEndpoint(
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway payPal,
        ILogger<CreatePaymentMethodEndpoint> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.Card?.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
        {
            return Results.BadRequest(new { message = "Card number and expiry (YYYY-MM) are required." });
        }

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        var isNewBuyer = buyer == null;
        buyer ??= new Buyer(buyerId);

        // A stable idempotency key for this single request so a retried save doesn't double-vault.
        var idempotencyKey = $"vault-{request.CorrelationId()}";
        var vaulted = await _payPal.VaultCardAsync(request.Card.ToCardDetails(), buyer.PayPalCustomerId, idempotencyKey);

        if (!string.IsNullOrEmpty(vaulted.PayPalCustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
        }

        var paymentMethod = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, request.Alias);

        if (isNewBuyer)
        {
            await _buyerRepository.AddAsync(buyer);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer);
        }

        _logger.LogInformation("Saved card {PaymentMethodId} ({Brand} ****{Last4}) for buyer.",
            paymentMethod.Id, vaulted.Brand, vaulted.Last4);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = PaymentMethodDto.From(paymentMethod)
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}

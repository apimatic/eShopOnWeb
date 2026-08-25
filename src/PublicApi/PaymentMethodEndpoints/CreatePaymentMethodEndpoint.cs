using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequestBody
{
    public CardDetailsRequestDto Card { get; set; } = new();
    public string? Alias { get; set; }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CreatePaymentMethodRequest(CreatePaymentMethodRequestBody body, string buyerId)
    {
        Body = body;
        BuyerId = buyerId;
    }

    public CreatePaymentMethodRequestBody Body { get; }
    public string BuyerId { get; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? Alias { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's Vault so it can be reused to pay for
/// a later order. Only PayPal's vault token id and safe display details (brand/last4/expiry)
/// are stored - full card details are never persisted by this app.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequestBody body, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new CreatePaymentMethodRequest(body, user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, PaymentDependencies deps)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var buyer = await deps.BuyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(request.BuyerId));
        var isNewBuyer = buyer == null;
        buyer ??= new Buyer(request.BuyerId);

        PayPalVaultedCard vaulted;
        try
        {
            vaulted = await deps.PayPalClient.VaultCardAsync(request.Body.Card.ToPayPalCardDetails(), request.BuyerId, Guid.NewGuid().ToString());
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: ex.ErrorName ?? "Could not save card with PayPal");
        }

        var paymentMethod = buyer.AddPaymentMethod(vaulted.PaymentTokenId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, request.Body.Alias);

        if (isNewBuyer)
        {
            await deps.BuyerRepository.AddAsync(buyer);
        }
        else
        {
            await deps.BuyerRepository.UpdateAsync(buyer);
        }

        response.PaymentMethodId = paymentMethod.Id;
        response.Brand = paymentMethod.Brand;
        response.Last4 = paymentMethod.Last4;
        response.Expiry = paymentMethod.Expiry;
        response.Alias = paymentMethod.Alias;

        return Results.Created("api/payment-methods", response);
    }
}

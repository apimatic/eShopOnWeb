using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper. The card is vaulted with PayPal; only a safe descriptor is stored.</summary>
public class CreatePaymentMethodRequest : BaseRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();

    public CardRequest ToCardRequest() => new()
    {
        CardNumber = CardNumber,
        ExpiryMonth = ExpiryMonth,
        ExpiryYear = ExpiryYear,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddress = BillingAddress
    };
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Identifier of the saved card, returned as a top-level field so it can be used to pay later.</summary>
    public int PaymentMethodId { get; set; }

    public SavedCardDto Card { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        SavedPaymentMethod saved;
        try
        {
            saved = await paymentMethodService.SaveCardAsync(buyerId, request.ToCardRequest().ToCardDetails());
        }
        catch (PaymentGatewayException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Card = SavedCardDto.From(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

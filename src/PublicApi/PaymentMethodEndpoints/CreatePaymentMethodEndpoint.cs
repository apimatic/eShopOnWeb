using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and describes it only by safe details (brand, last digits, expiry).
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public CreatePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var saved = await _paymentService.SavePaymentMethodAsync(buyerId, request.Card.ToCardDetails());
            return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = saved.Id,
                Brand = saved.Brand,
                LastDigits = saved.LastDigits,
                Expiry = saved.Expiry,
                CardholderName = saved.CardholderName
            });
        }
        catch (Exception ex) when (PaymentEndpointHelpers.TryMapException(ex) is { } result)
        {
            return result;
        }
    }
}

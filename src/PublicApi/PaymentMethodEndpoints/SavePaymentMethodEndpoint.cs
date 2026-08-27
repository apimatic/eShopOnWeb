using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card at PayPal for the signed-in shopper. The response carries only safe
/// display attributes (brand, last digits) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        var saved = await paymentService.SaveCardAsync(CreateOrderEndpoint.GetBuyerId(user), request.Card);

        response.PaymentMethodId = saved.PaymentMethodId;
        response.Brand = saved.Brand;
        response.LastDigits = saved.LastDigits;
        response.Expiry = saved.Expiry;
        response.CardholderName = saved.CardholderName;

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    [Required]
    public CardDetails Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SavePaymentMethodResponse()
    {
    }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

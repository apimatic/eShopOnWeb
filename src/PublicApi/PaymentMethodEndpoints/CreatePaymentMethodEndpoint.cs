using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Identifier of the saved card that was created.</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely (brand + last four); full card details are never stored or returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http, IPaymentMethodService service) =>
            {
                request.BuyerId = CallerIdentity.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        try
        {
            var card = new PayPalCardDetails(request.Number, request.Expiry, request.SecurityCode, request.CardholderName);
            var paymentMethod = await service.SaveCardAsync(request.BuyerId, card);

            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = paymentMethod.Id,
                PaymentMethod = PaymentMethodDto.From(paymentMethod)
            };
            return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly IPayPalPaymentService _payPal;

    public CreatePaymentMethodEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext ctx, CancellationToken ct) =>
            {
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.Number) || string.IsNullOrEmpty(request.Expiry) || string.IsNullOrEmpty(request.SecurityCode))
            return Results.BadRequest("Card number, expiry and security code are required.");

        var idempotencyKey = $"save-{request.BuyerId}-{Guid.NewGuid():N}";
        var card = new CardDetails(
            Number: request.Number,
            Expiry: request.Expiry,
            SecurityCode: request.SecurityCode,
            Name: request.CardholderName,
            BillingCountryCode: request.BillingCountryCode ?? "US");

        try
        {
            var result = await _payPal.SaveCardAsync(
                merchantCustomerId: request.BuyerId,
                card: card,
                idempotencyKey: idempotencyKey);

            return Results.Created($"/api/payment-methods/{result.PaymentTokenId}",
                new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = result.PaymentTokenId,
                    LastFourDigits = result.LastFourDigits,
                    CardBrand = result.CardBrand,
                    Expiry = result.Expiry
                });
        }
        catch (PayPalOperationException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Card save error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? LastFourDigits { get; set; }
    public string? CardBrand { get; set; }
    public string? Expiry { get; set; }
}

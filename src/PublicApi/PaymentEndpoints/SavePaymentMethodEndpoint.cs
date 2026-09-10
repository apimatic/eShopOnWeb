using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record SavePaymentMethodCommand(string BuyerId, SavePaymentMethodRequest Body, CancellationToken Ct);

/// <summary>
/// POST /api/payment-methods — a logged-in shopper saves a card. The response identifies the saved card
/// and describes it safely (brand + last digits), never full card details. Returns paymentMethodId.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new SavePaymentMethodCommand(buyerId, request, http.RequestAborted), service);
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodCommand command, IPaymentApplicationService service)
    {
        if (command.Body?.Card is null)
            throw new PaymentValidationException("Card details are required to save a card.");

        var saved = await service.SaveCardAsync(command.BuyerId,
            PaymentMapper.ToDomain(command.Body.Card),
            PaymentMapper.ToDomain(command.Body.BillingAddress), command.Ct);

        var dto = PaymentMapper.ToDto(saved);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new
        {
            paymentMethodId = dto.PaymentMethodId,
            brand = dto.Brand,
            lastDigits = dto.LastDigits,
            expiry = dto.Expiry,
            cardholderName = dto.CardholderName
        });
    }
}

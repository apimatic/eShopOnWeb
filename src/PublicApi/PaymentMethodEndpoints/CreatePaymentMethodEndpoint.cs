using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it at PayPal. Only a token plus a safe
/// descriptor (brand / last four / expiry) is stored locally; full card details never touch the
/// application database.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Card is null)
        {
            return Results.BadRequest("A 'card' is required.");
        }
        if (!request.Card.TryValidate(out var error))
        {
            return Results.BadRequest(error);
        }

        var gateway = http.RequestServices.GetRequiredService<IPaymentGatewayService>();
        var repository = http.RequestServices.GetRequiredService<IRepository<SavedPaymentMethod>>();

        VaultedCard vaulted;
        try
        {
            vaulted = await gateway.VaultCardAsync(request.Card.ToCardDetails(), http.RequestAborted);
        }
        catch (PaymentGatewayException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Could not save card");
        }

        var saved = new SavedPaymentMethod(
            buyerId: buyerId,
            paymentTokenId: vaulted.PaymentTokenId,
            cardBrand: vaulted.Brand,
            lastFourDigits: vaulted.LastFourDigits,
            cardExpiry: string.IsNullOrWhiteSpace(vaulted.Expiry) ? request.Card.Expiry : vaulted.Expiry,
            cardholderName: vaulted.CardholderName,
            providerCustomerId: vaulted.CustomerId);

        saved = await repository.AddAsync(saved);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = SavedCardDto.FromEntity(saved)
        };

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

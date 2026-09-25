using System.Security.Claims;
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

public record SavePaymentMethodRequest(string BuyerId, SavePaymentMethodBody Body, CancellationToken Ct);

/// <summary>POST /api/payment-methods — save a card for the signed-in shopper.</summary>
public class PaymentMethodsSaveEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodBody body, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(new SavePaymentMethodRequest(user.BuyerId(), body, ct), service))
            .Produces<SavePaymentMethodApiResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (request.Body?.Card is null)
        {
            throw new PaymentOperationException("A card is required to save a payment method.", PaymentErrorKind.Validation);
        }

        var view = await service.SaveAsync(request.BuyerId, request.Body.Card.ToCardInput(), request.Ct);
        var response = new SavePaymentMethodApiResponse(view.PaymentMethodId, view.Brand, view.Last4, view.Expiry, view.CardholderName);
        return Results.Created($"api/payment-methods/{view.PaymentMethodId}", response);
    }
}

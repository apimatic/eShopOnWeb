using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Route id for deleting a saved card.</summary>
public record PaymentMethodIdCommand(int PaymentMethodId);

/// <summary>
/// Shopper action. Removes a saved card. Afterwards it no longer appears among the caller's saved
/// cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, PaymentMethodIdCommand, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService) =>
                await HandleAsync(new PaymentMethodIdCommand(paymentMethodId), savedCardService))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodIdCommand command, ISavedCardService savedCardService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);
        await savedCardService.DeleteCardAsync(buyerId, command.PaymentMethodId);
        return Results.NoContent();
    }
}

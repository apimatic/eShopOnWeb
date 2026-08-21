using System;
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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely; the new card's id is returned as a top-level <c>paymentMethodId</c>.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService service,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    throw new OrderValidationException("A card ('card') with a number is required to save a card.");
                }

                var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardPaymentDetails(), ct);

                var response = new SavePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    PaymentMethod = SavedPaymentMethodDto.From(saved)
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

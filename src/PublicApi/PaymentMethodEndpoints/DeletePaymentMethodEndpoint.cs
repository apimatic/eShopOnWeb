using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Status { get; set; } = "Deleted";
}

/// <summary>
/// Removes a saved card: deletes it from PayPal's vault and from the caller's saved cards.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<SavedCard> _savedCardRepository;

    public DeletePaymentMethodEndpoint(IPayPalGateway payPalGateway, IRepository<SavedCard> savedCardRepository)
    {
        _payPalGateway = payPalGateway;
        _savedCardRepository = savedCardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
            {
                var request = new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId };
                return await HandleAsync(request, user);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;

        var savedCard = await _savedCardRepository.GetByIdAsync(request.PaymentMethodId);
        // A shopper must never see or delete another shopper's card: behave as if it does not exist.
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new NotFoundException(request.PaymentMethodId.ToString(), nameof(SavedCard));
        }

        await _payPalGateway.DeletePaymentTokenAsync(savedCard.VaultPaymentTokenId);
        await _savedCardRepository.DeleteAsync(savedCard);

        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId
        };
        return Results.Ok(response);
    }
}

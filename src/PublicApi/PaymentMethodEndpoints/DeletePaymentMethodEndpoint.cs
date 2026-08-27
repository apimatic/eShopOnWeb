using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }

    /// <summary>Populated from the JWT; never read from the request.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public DeletePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's saved cards, both locally and from PayPal's vault.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = user.Identity?.Name },
                    paymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        await paymentMethodService.DeleteAsync(request.BuyerId, request.PaymentMethodId);

        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId,
            Deleted = true
        };
        return Results.Ok(response);
    }
}

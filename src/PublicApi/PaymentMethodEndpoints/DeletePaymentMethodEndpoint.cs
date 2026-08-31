using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, from the app and from PayPal's vault.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    Username = OrderMapping.GetUserName(user)
                }, paymentMethodService);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }

        try
        {
            var deleted = await paymentMethodService.DeleteAsync(request.Username, request.PaymentMethodId);
            return deleted
                ? Results.NoContent()
                : Results.NotFound(new DeletePaymentMethodResponse { Message = $"Saved card {request.PaymentMethodId} was not found." });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new DeletePaymentMethodResponse { Message = ex.Message });
        }
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public int PaymentMethodId { get; set; }

    [JsonIgnore]
    public string? Username { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public string? Message { get; set; }
}

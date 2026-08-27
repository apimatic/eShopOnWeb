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
    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public int PaymentMethodId { get; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the
/// caller's saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService, ClaimsPrincipal user) =>
            {
                var request = new DeletePaymentMethodRequest(paymentMethodId) { BuyerId = user.GetBuyerId() };
                return await HandleAsync(request, savedCardService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());

        if (request.BuyerId == null)
        {
            return Results.Unauthorized();
        }

        var deleted = await savedCardService.DeleteAsync(request.BuyerId, request.PaymentMethodId, default);
        if (!deleted)
        {
            return Results.NotFound();
        }

        response.PaymentMethodId = request.PaymentMethodId;
        response.Deleted = true;
        return Results.Ok(response);
    }
}

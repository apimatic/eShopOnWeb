using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it is no longer listed and can
/// no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public DeletePaymentMethodEndpoint(IRepository<SavedPaymentMethod> paymentMethodRepository, IPaymentGateway paymentGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, user);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());

        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await _paymentMethodRepository.GetByIdAsync(request.PaymentMethodId);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == 404)
        {
            // Already gone at PayPal; still remove it locally.
        }
        catch (PaymentGatewayException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        await _paymentMethodRepository.DeleteAsync(savedCard);

        response.PaymentMethodId = request.PaymentMethodId;
        response.Deleted = true;
        return Results.Ok(response);
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

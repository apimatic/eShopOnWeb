using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes a saved card: deletes the PayPal vault token and the local record.
/// Afterwards it is neither listed nor usable to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<DeletePaymentMethodEndpoint> _logger;

    public DeletePaymentMethodEndpoint(IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        ILogger<DeletePaymentMethodEndpoint> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) => await HandleAsync(
                new DeletePaymentMethodRequest(paymentMethodId, user.Identity?.Name ?? string.Empty)))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var savedCard = await _paymentMethodRepository.GetByIdAsync(request.PaymentMethodId);
        if (savedCard is null || savedCard.BuyerId != request.BuyerId)
        {
            return Results.NotFound(new { message = $"Payment method {request.PaymentMethodId} not found." });
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal; still remove the local record.
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Deleting vault token failed: {Error} {Issue} (debug {DebugId})",
                ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        await _paymentMethodRepository.DeleteAsync(savedCard);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId, string buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }

    public int PaymentMethodId { get; }
    public string BuyerId { get; }
}

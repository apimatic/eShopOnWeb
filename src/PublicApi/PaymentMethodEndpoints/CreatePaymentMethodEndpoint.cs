using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    /// <summary>Optional shopper-friendly label for the card.</summary>
    public string? Alias { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }
}

public class BillingAddressInput
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class CreatePaymentMethodResponse
{
    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it at PayPal. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details, which
/// are not stored in this app's database.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly ISavedCardService _savedCardService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(ISavedCardService savedCardService, IHttpContextAccessor httpContextAccessor)
    {
        _savedCardService = savedCardService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request) => await HandleAsync(request))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        var card = new CardDetails(
            request.Number,
            request.Expiry,
            request.SecurityCode,
            request.Name,
            request.BillingAddress is null ? null : new CardBillingAddress(
                request.BillingAddress.AddressLine1,
                request.BillingAddress.AddressLine2,
                request.BillingAddress.AdminArea2,
                request.BillingAddress.AdminArea1,
                request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode));

        var method = await _savedCardService.SaveCardAsync(buyerId, card, request.Alias);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

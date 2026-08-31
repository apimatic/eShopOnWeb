using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card at PayPal for the signed-in shopper. Only safe display
/// data (brand, last digits, expiry) is stored and returned — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<CreatePaymentMethodEndpoint> _logger;

    public CreatePaymentMethodEndpoint(IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        ILogger<CreatePaymentMethodEndpoint> logger)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
        {
            return Results.BadRequest(new { message = "Card number and expiry (YYYY-MM) are required." });
        }

        VaultedCardResult vaulted;
        try
        {
            vaulted = await _paymentGateway.VaultCardAsync(new CardDetails
            {
                Number = request.Number,
                Expiry = request.Expiry,
                SecurityCode = request.SecurityCode,
                HolderName = request.Name,
                AddressLine1 = request.BillingAddressLine1,
                AdminArea2 = request.BillingCity,
                AdminArea1 = request.BillingState,
                PostalCode = request.BillingPostalCode,
                CountryCode = request.BillingCountryCode
            }, request.BuyerId, $"eshop-vault-{request.BuyerId}-{Guid.NewGuid():N}");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Vaulting a card failed: {Error} {Issue} (debug {DebugId})",
                ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var saved = new SavedPaymentMethod(request.BuyerId, vaulted.VaultTokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry);
        saved = await _paymentMethodRepository.AddAsync(saved);

        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    /// <summary>Set from the JWT; never accepted from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
}

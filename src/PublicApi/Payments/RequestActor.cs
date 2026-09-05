using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Who is asking. Identity comes from the JWT and nowhere else, so a caller can only ever act as
/// themselves; anything that belongs to somebody else is reported as not found. Operator-only routes
/// are gated by role on the route itself.
/// </summary>
public class RequestActor
{
    private RequestActor(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }

    public static RequestActor From(ClaimsPrincipal principal)
    {
        var buyerId = principal.Identity?.Name ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ResourceNotFoundException("The caller of this request could not be identified.");
        }

        return new RequestActor(buyerId);
    }

    /// <summary>
    /// The card a shopper sent, turned into what the gateway needs. Null when the shopper named one of
    /// their saved cards instead. Nothing here outlives the request.
    /// </summary>
    public CardDetails? ToCardDetails(PaymentCardDto? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardDetails
        {
            Number = Digits(card.Number),
            Expiry = Collapse(card.Expiry),
            SecurityCode = Collapse(card.SecurityCode),
            CardHolderName = card.CardHolderName?.Trim() ?? string.Empty,
            Street = Trimmed(card.Street),
            City = Trimmed(card.City),
            Region = Trimmed(card.Region),
            PostalCode = Trimmed(card.PostalCode),
            CountryCode = Trimmed(card.CountryCode)
        };
    }

    private static string Digits(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    private static string Collapse(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A card as a caller sends it. It exists only long enough to be handed to the processor, and
/// <see cref="ToString"/> is redacted so it cannot reach a log through interpolation.
/// </summary>
public class PaymentCardDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardHolderName { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public override string ToString() => "PaymentCardDto([redacted])";
}

/// <summary>
/// A request answered only for the shopper the token identifies. The route fills in
/// <see cref="Actor"/> from the JWT, never from the body.
/// </summary>
public abstract class ShopperRequest : BaseRequest
{
    public RequestActor? Actor { get; set; }

    public RequestActor RequireActor() => Actor
        ?? throw new InvalidOperationException("The caller of this request was not identified.");
}

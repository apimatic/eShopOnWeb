using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider up front —
/// a number the provider does not consider a usable destination is rejected here, not later when a message
/// fails — and what gets stored is the provider's own canonical (E.164) form, not the caller's raw input.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ISmsProvider smsProvider, IRepository<ContactNumber> repository, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, smsProvider, repository, user, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request,
        ISmsProvider smsProvider,
        IRepository<ContactNumber> repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.UserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        PhoneValidationResult validation;
        try
        {
            validation = await smsProvider.ValidateNumberAsync(request.PhoneNumber, ct);
        }
        catch (SmsProviderException ex)
        {
            // A provider outage while validating — not a rejection of the number itself.
            return ProviderErrorResults.From(ex);
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                message = "The number is not a usable destination and was not registered.",
                reasons = validation.Reasons
            });
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await repository.AddAsync(contactNumber, ct);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

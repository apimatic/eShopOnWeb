using System;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number the caller typed. Validated with the provider before anything is stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonIgnore]
    public string BuyerId { get; private set; } = string.Empty;

    public void SetBuyerId(string buyerId) => BuyerId = buyerId;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number, as stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here, and the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service, CancellationToken cancellationToken) =>
            {
                request.SetBuyerId(user.GetBuyerId());
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest("A phone number is required.");

        var result = await service.RegisterAsync(request.BuyerId, request.PhoneNumber, cancellationToken);
        if (!result.IsValid)
            return Results.BadRequest("The phone number is not a usable destination and was not registered.");

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumberId!.Value,
            PhoneNumber = result.CanonicalNumber!
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

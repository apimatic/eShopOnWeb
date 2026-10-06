using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.OrderCustomAttributes;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// The "Gift message" field on Square orders: an order custom attribute that Square staff can see and edit.
/// The value lives only in Square.
/// </summary>
public sealed class SquareGiftMessageField
{
    private static readonly TimeSpan EnsuredCacheLifetime = TimeSpan.FromHours(1);

    private readonly SquareClientHolder _clients;
    private readonly SquareLeaseStore _leases;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SquareGiftMessageField> _logger;

    public SquareGiftMessageField(SquareClientHolder clients, SquareLeaseStore leases, IMemoryCache cache, ILogger<SquareGiftMessageField> logger)
    {
        _clients = clients;
        _leases = leases;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Makes sure the merchant has the gift-message field (created once per merchant).</summary>
    public async Task EnsureDefinitionAsync(string merchantId, CancellationToken cancellationToken)
    {
        var cacheKey = $"square:gift-definition:{merchantId}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (await DefinitionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                _cache.Set(cacheKey, true, EnsuredCacheLifetime);
                return;
            }

            await using var lease = await _leases.TryAcquireAsync($"gift-definition:{merchantId}", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                // Another request is creating it; wait and read again.
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            await CreateDefinitionAsync(cancellationToken).ConfigureAwait(false);
            _cache.Set(cacheKey, true, EnsuredCacheLifetime);
            return;
        }

        throw new SquareIntegrationException(SquareFailureKind.Unavailable, "The Square gift-message field could not be set up. Try again.");
    }

    public async Task SetAsync(string squareOrderId, string giftMessage, string idempotencyKey, CancellationToken cancellationToken)
    {
        await SquareCall.RunAsync("OrderCustomAttributes.UpsertOrderCustomAttribute", ct => _clients.Client.OrderCustomAttributes.UpsertOrderCustomAttribute(
            new UpsertOrderCustomAttributeOperationRequest
            {
                OrderId = squareOrderId,
                CustomAttributeKey = SquareConstants.GiftMessageAttributeKey,
                Body = new UpsertOrderCustomAttributeRequest
                {
                    CustomAttribute = new CustomAttribute { Value = giftMessage },
                    IdempotencyKey = idempotencyKey,
                },
            },
            cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The gift message as Square holds it now (null when the order has none).</summary>
    public async Task<string?> GetAsync(string squareOrderId, CancellationToken cancellationToken)
    {
        RetrieveOrderCustomAttributeResponse response;
        try
        {
            response = await SquareCall.RunAsync("OrderCustomAttributes.RetrieveOrderCustomAttribute", ct => _clients.Client.OrderCustomAttributes.RetrieveOrderCustomAttribute(
                new RetrieveOrderCustomAttributeRequest
                {
                    OrderId = squareOrderId,
                    CustomAttributeKey = SquareConstants.GiftMessageAttributeKey,
                },
                cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.IsNotFound)
        {
            return null;
        }

        return response.CustomAttribute?.Value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement element => element.GetRawText(),
            var other => other.ToString(),
        };
    }

    private async Task<bool> DefinitionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SquareCall.RunAsync("OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition",
                ct => _clients.Client.OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition(
                    new RetrieveOrderCustomAttributeDefinitionRequest { Key = SquareConstants.GiftMessageAttributeKey },
                    cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);
            return response.CustomAttributeDefinition is not null;
        }
        catch (SquareIntegrationException ex) when (ex.IsNotFound)
        {
            return false;
        }
    }

    private async Task CreateDefinitionAsync(CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(SquareConstants.TextAttributeSchemaJson);
        var request = new CreateOrderCustomAttributeDefinitionOperationRequest
        {
            Body = new CreateOrderCustomAttributeDefinitionRequest
            {
                IdempotencyKey = Guid.NewGuid().ToString(),
                CustomAttributeDefinition = new CustomAttributeDefinition
                {
                    Key = SquareConstants.GiftMessageAttributeKey,
                    Name = SquareConstants.GiftMessageAttributeName,
                    Description = SquareConstants.GiftMessageAttributeDescription,
                    Visibility = CustomAttributeDefinitionVisibility.VisibilityReadWriteValues,
                    Schema = schema.RootElement.Clone(),
                },
            },
        };

        try
        {
            await SquareCall.RunAsync("OrderCustomAttributes.CreateOrderCustomAttributeDefinition",
                ct => _clients.Client.OrderCustomAttributes.CreateOrderCustomAttributeDefinition(request, cancellationToken: ct),
                _logger, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created the Square order field '{Name}' ({Key})", SquareConstants.GiftMessageAttributeName, SquareConstants.GiftMessageAttributeKey);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare || ex.Kind == SquareFailureKind.Rejected)
        {
            // Unknown outcome, or Square refused because the key already exists: settle by reading it back.
            if (!await DefinitionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.OrderCustomAttributes;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Keeps the shopper's gift message on the Square order as an order custom attribute labelled "Gift message",
/// which Square staff can see and edit. eShop never stores the message itself.
/// </summary>
public sealed class SquareGiftMessageStore
{
    public const string Key = "gift-message";
    public const string Label = "Gift message";
    public const int MaxLength = 200;

    /// <summary>Schema of a Square custom field that holds text (as specified by Square's account team).</summary>
    private const string StringSchemaRef =
        "https://developer-production-s.squarecdn.com/schemas/v1/common.json#squareup.common.String";

    private readonly SquareClientProvider _clients;
    private readonly ILogger<SquareGiftMessageStore> _logger;
    private readonly ConcurrentDictionary<string, bool> _definedFor = new(StringComparer.Ordinal);

    public SquareGiftMessageStore(SquareClientProvider clients, ILogger<SquareGiftMessageStore> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    /// <summary>Makes sure the merchant has the "Gift message" order field (created once per merchant).</summary>
    public async Task EnsureDefinitionAsync(string merchantId, CancellationToken cancellationToken)
    {
        if (_definedFor.ContainsKey(merchantId)) return;

        if (!await DefinitionExistsAsync(cancellationToken))
        {
            try
            {
                await _clients.Merchant.OrderCustomAttributes.CreateOrderCustomAttributeDefinition(
                    new CreateOrderCustomAttributeDefinitionOperationRequest
                    {
                        Body = new CreateOrderCustomAttributeDefinitionRequest
                        {
                            IdempotencyKey = Guid.NewGuid().ToString(),
                            CustomAttributeDefinition = new CustomAttributeDefinition
                            {
                                Key = Key,
                                Name = Label,
                                Description = "Gift message the shopper wrote at online checkout (eShopOnWeb).",
                                Schema = new Dictionary<string, string> { ["$ref"] = StringSchemaRef },
                                Visibility = CustomAttributeDefinitionVisibility.VisibilityReadWriteValues,
                            },
                        },
                    }, cancellationToken: cancellationToken);
                _logger.LogInformation("Created the Square order field '{Label}' for merchant {MerchantId}.", Label, merchantId);
            }
            catch (SdkException ex) when (ex is SdkConnectionException or ApiException<RawError>)
            {
                // Unknown outcome, or a concurrent creator won (conflict): settle by reading the definition.
                if (!await DefinitionExistsAsync(cancellationToken))
                    throw SquareErrors.Translate(ex, "creating the Square gift message field", isWrite: true);
            }
            catch (SdkException ex)
            {
                throw SquareErrors.Translate(ex, "creating the Square gift message field", isWrite: true);
            }
        }

        _definedFor[merchantId] = true;
    }

    /// <summary>
    /// Sets the gift message on a Square order. Square requires the current <c>version</c> to change a value that
    /// already exists, so an existing value is updated with its version.
    /// </summary>
    public async Task SetAsync(string squareOrderId, string message, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await UpsertAsync(squareOrderId, message, idempotencyKey, version: null, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            // Unknown outcome: read what Square holds now. The same text means the write landed; otherwise write
            // again on top of whatever version is there.
            _logger.LogWarning(ex, "Gift message write on Square order {SquareOrderId} has an unknown outcome; reading it back.", squareOrderId);
            try
            {
                var current = await RetrieveAsync(squareOrderId, cancellationToken);
                if (current is not null && ReadText(current.Value) == message) return;
                await UpsertAsync(squareOrderId, message, idempotencyKey + "-r", current?.Version, cancellationToken);
            }
            catch (SdkException retry)
            {
                throw SquareErrors.Translate(retry, "saving the gift message in Square", isWrite: true);
            }
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "saving the gift message in Square", isWrite: true);
        }
    }

    private Task<UpsertOrderCustomAttributeResponse> UpsertAsync(string squareOrderId, string message, string idempotencyKey,
        int? version, CancellationToken cancellationToken) =>
        _clients.Merchant.OrderCustomAttributes.UpsertOrderCustomAttribute(new UpsertOrderCustomAttributeOperationRequest
        {
            OrderId = squareOrderId,
            CustomAttributeKey = Key,
            Body = new UpsertOrderCustomAttributeRequest
            {
                IdempotencyKey = idempotencyKey,
                CustomAttribute = new CustomAttribute { Value = message, Version = version },
            },
        }, cancellationToken: cancellationToken);

    /// <summary>The gift message as Square holds it now, or null when the order has none.</summary>
    public async Task<string?> GetAsync(string squareOrderId, CancellationToken cancellationToken)
    {
        try
        {
            return ReadText((await RetrieveAsync(squareOrderId, cancellationToken))?.Value);
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "reading the gift message from Square");
        }
    }

    private async Task<CustomAttribute?> RetrieveAsync(string squareOrderId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _clients.Merchant.OrderCustomAttributes.RetrieveOrderCustomAttribute(
                new RetrieveOrderCustomAttributeRequest { OrderId = squareOrderId, CustomAttributeKey = Key },
                cancellationToken: cancellationToken);
            return response.CustomAttribute;
        }
        catch (ApiException<RawError> ex) when (SquareErrors.IsNotFound(ex))
        {
            return null;
        }
    }

    private async Task<bool> DefinitionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _clients.Merchant.OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition(
                new RetrieveOrderCustomAttributeDefinitionRequest { Key = Key }, cancellationToken: cancellationToken);
            return response.CustomAttributeDefinition is not null;
        }
        catch (ApiException<RawError> ex) when (SquareErrors.IsNotFound(ex))
        {
            return false;
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "reading the Square gift message field");
        }
    }

    private static string? ReadText(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element => element.GetRawText(),
        _ => value.ToString(),
    };
}

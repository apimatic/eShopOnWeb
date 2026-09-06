using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Declares which schema of the Maxio OpenAPI specification a model mirrors. The spec is the
/// contract for this integration, and the conformance tests use this attribute to prove that
/// every property we send or read actually exists in that schema.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MaxioSchemaAttribute : Attribute
{
    public MaxioSchemaAttribute(string schemaName)
    {
        SchemaName = schemaName;
    }

    /// <summary>File name (without extension) under maxio-spec/components/schemas.</summary>
    public string SchemaName { get; }
}

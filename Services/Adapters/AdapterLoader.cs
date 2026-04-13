using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Splash.Services.Adapters;

/// <summary>
/// Loads splash adapter YAML files (schema v1) into Adapter instances.
/// Adapter YAMLs ship as embedded resources under Splash.adapters.*.yaml.
///
/// Uses StaticDeserializerBuilder + AdapterStaticContext so deserialization
/// is AOT-safe. Switching back to the reflection-based DeserializerBuilder
/// would re-introduce IL3050 and break `dotnet publish --publish-aot`.
/// </summary>
public static class AdapterLoader
{
    private const int SupportedSchemaVersion = 1;

    private static readonly IDeserializer _deserializer =
        new StaticDeserializerBuilder(new AdapterStaticContext())
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    /// <summary>
    /// Parse a YAML string into an Adapter. Throws InvalidOperationException
    /// on schema version mismatch or missing required fields.
    /// </summary>
    public static Adapter Parse(string yaml, string sourceName)
    {
        Adapter adapter;
        try
        {
            adapter = _deserializer.Deserialize<Adapter>(yaml)
                ?? throw new InvalidOperationException($"Adapter YAML '{sourceName}' deserialized to null");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to parse adapter YAML '{sourceName}': {ex.Message}", ex);
        }

        if (adapter.Schema != SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"Adapter '{sourceName}' declares schema v{adapter.Schema}, but this splash build supports v{SupportedSchemaVersion}.");

        if (string.IsNullOrEmpty(adapter.Name))
            throw new InvalidOperationException($"Adapter '{sourceName}' has empty 'name' field");

        if (string.IsNullOrEmpty(adapter.Family))
            throw new InvalidOperationException($"Adapter '{sourceName}' (name={adapter.Name}) has empty 'family' field");

        return adapter;
    }

    /// <summary>
    /// Load all adapter YAMLs embedded in this assembly. Returns the list
    /// of successfully parsed adapters and a list of (resourceName, error)
    /// pairs for any that failed.
    /// </summary>
    public static LoadResult LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var adapters = new List<Adapter>();
        var errors = new List<(string Resource, string Error)>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith("Splash.adapters.", StringComparison.Ordinal))
                continue;
            if (!resourceName.EndsWith(".yaml", StringComparison.Ordinal))
                continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException("resource stream was null");
                using var reader = new StreamReader(stream);
                var yaml = reader.ReadToEnd();
                var adapter = Parse(yaml, resourceName);
                adapters.Add(adapter);
            }
            catch (Exception ex)
            {
                errors.Add((resourceName, ex.Message));
            }
        }

        return new LoadResult(adapters, errors);
    }

    public record LoadResult(
        IReadOnlyList<Adapter> Adapters,
        IReadOnlyList<(string Resource, string Error)> Errors);
}

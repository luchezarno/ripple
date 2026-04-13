namespace Splash.Services.Adapters;

/// <summary>
/// Immutable in-memory registry of loaded adapters, keyed by canonical name
/// and alias. Constructed once at startup via AdapterRegistry.LoadEmbedded.
///
/// Lookup is case-insensitive on the shell family name, matching the
/// existing ConsoleManager.NormalizeShellFamily convention (bash, pwsh, cmd,
/// zsh, powershell, etc.).
/// </summary>
public sealed class AdapterRegistry
{
    private readonly Dictionary<string, Adapter> _byName;

    /// <summary>
    /// Process-wide default registry, initialized once at startup by
    /// Program.cs. Read by ConsoleWorker / ConsoleManager to look up the
    /// adapter for a shell without plumbing the registry through
    /// constructors. Null until Program.cs calls SetDefault.
    /// </summary>
    public static AdapterRegistry? Default { get; private set; }

    public static void SetDefault(AdapterRegistry registry) => Default = registry;

    private AdapterRegistry(Dictionary<string, Adapter> byName)
    {
        _byName = byName;
    }

    /// <summary>
    /// Load all adapters embedded in the splash assembly and build a
    /// registry. Throws if any adapter name collides with a previously
    /// registered name or alias. Parse errors for individual adapters are
    /// surfaced via the returned LoadReport, not thrown.
    /// </summary>
    public static (AdapterRegistry Registry, LoadReport Report) LoadEmbedded()
    {
        var loadResult = AdapterLoader.LoadEmbedded();
        var byName = new Dictionary<string, Adapter>(StringComparer.OrdinalIgnoreCase);
        var collisions = new List<string>();

        foreach (var adapter in loadResult.Adapters)
        {
            if (!byName.TryAdd(adapter.Name, adapter))
            {
                collisions.Add($"adapter name '{adapter.Name}' is registered by multiple YAML files");
                continue;
            }

            if (adapter.Aliases != null)
            {
                foreach (var alias in adapter.Aliases)
                {
                    if (!byName.TryAdd(alias, adapter))
                        collisions.Add($"alias '{alias}' for adapter '{adapter.Name}' collides with existing registration");
                }
            }
        }

        var registry = new AdapterRegistry(byName);
        var report = new LoadReport(
            Loaded: loadResult.Adapters.Select(a => a.Name).ToList(),
            ParseErrors: loadResult.Errors,
            Collisions: collisions);

        return (registry, report);
    }

    /// <summary>
    /// Look up an adapter by shell family name or alias. Returns null if
    /// no adapter matches. Name comparison is case-insensitive.
    /// </summary>
    public Adapter? Find(string name)
        => _byName.TryGetValue(name, out var adapter) ? adapter : null;

    public IReadOnlyCollection<Adapter> All => _byName.Values.Distinct().ToList();

    public int Count => _byName.Values.Distinct().Count();

    public record LoadReport(
        IReadOnlyList<string> Loaded,
        IReadOnlyList<(string Resource, string Error)> ParseErrors,
        IReadOnlyList<string> Collisions)
    {
        public bool HasErrors => ParseErrors.Count > 0 || Collisions.Count > 0;

        public string Summary()
        {
            var parts = new List<string>
            {
                $"{Loaded.Count} loaded ({string.Join(", ", Loaded)})"
            };
            if (ParseErrors.Count > 0)
                parts.Add($"{ParseErrors.Count} parse error(s): {string.Join("; ", ParseErrors.Select(e => $"{e.Resource}: {e.Error}"))}");
            if (Collisions.Count > 0)
                parts.Add($"{Collisions.Count} collision(s): {string.Join("; ", Collisions)}");
            return string.Join(" | ", parts);
        }
    }
}

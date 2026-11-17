namespace JobSarthi.Api.Sources;

public class JobSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IJobSource> _sources;
    public JobSourceRegistry(IEnumerable<IJobSource> sources) =>
        _sources = sources.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

    public IJobSource? Get(string key) => _sources.GetValueOrDefault(key);
    public IEnumerable<IJobSource> All => _sources.Values;
}
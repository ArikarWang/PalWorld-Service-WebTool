using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PalWorldService.Shared.Config;

public class ConfigLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config not found: {path}");

        var yaml = File.ReadAllText(path);
        var config = _deserializer.Deserialize<AppConfig>(yaml)
            ?? throw new InvalidOperationException("Failed to parse config.");

        foreach (var s in config.Servers)
        {
            if (string.IsNullOrWhiteSpace(s.Id))
                throw new InvalidOperationException("Each server must have an id.");
            if (string.IsNullOrWhiteSpace(s.Name))
                s.Name = s.Id;
        }

        if (config.Servers.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate server id in config.");

        return config;
    }
}

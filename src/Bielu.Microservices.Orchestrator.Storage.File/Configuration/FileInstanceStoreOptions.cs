namespace Bielu.Microservices.Orchestrator.Storage.File.Configuration;

/// <summary>
/// Configuration options for the file-based instance store.
/// </summary>
public class FileInstanceStoreOptions
{
    /// <summary>
    /// The path to the JSON file used to persist instance state.
    /// </summary>
    public string FilePath { get; set; } = "orchestrator-state.json";
}

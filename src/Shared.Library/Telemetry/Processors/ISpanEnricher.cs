using System.Diagnostics;

namespace Shared.Library.Telemetry.Processors;

/// <summary>
/// Interface for components that enrich spans with additional information
/// </summary>
public interface ISpanEnricher
{
    /// <summary>
    /// Enriches a span when it starts 
    /// </summary>
    void EnrichSpanAtStart(Activity span);

    /// <summary>
    /// Enriches a span when it ends
    /// </summary>
    void EnrichSpanAtEnd(Activity span);
}

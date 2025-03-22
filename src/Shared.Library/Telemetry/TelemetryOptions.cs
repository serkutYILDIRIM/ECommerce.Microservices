using Shared.Library.Telemetry.Sampling;

namespace Shared.Library.Telemetry
{
    public class TelemetryOptions
    {
        public SamplerType SamplerType { get; set; } = SamplerType.AlwaysOn;
        public double SamplingProbability { get; set; } = 1.0;
        public bool UseCompositeSampling { get; set; } = false;
        public int MaxTracesPerSecond { get; set; } = 100;
        public List<SamplingRule> Rules { get; set; } = new List<SamplingRule>();
    }
}

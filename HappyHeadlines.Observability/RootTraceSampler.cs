using System.Diagnostics;
using OpenTelemetry.Trace;

namespace HappyHeadlines.Observability;

/// <summary>
/// Decides what happens to the FIRST span of a trace (ParentBasedSampler only asks this sampler
/// when there is no parent). Normally that is a random keep-ratio.
///
/// One exception: a RabbitMQ consumer span with no parent. When the publisher dropped its trace,
/// MassTransit sends no trace context, so the consumer would start a brand new trace and keep it
/// by chance. We drop it instead, so a dropped trace stays dropped on the other side of the queue.
/// </summary>
internal sealed class RootTraceSampler : Sampler
{
    private readonly TraceIdRatioBasedSampler _ratioSampler;

    public RootTraceSampler(double ratio)
    {
        _ratioSampler = new TraceIdRatioBasedSampler(ratio);
    }

    public override SamplingResult ShouldSample(in SamplingParameters parameters)
    {
        if (parameters.Kind == ActivityKind.Consumer)
            return new SamplingResult(SamplingDecision.Drop);

        return _ratioSampler.ShouldSample(parameters);
    }
}

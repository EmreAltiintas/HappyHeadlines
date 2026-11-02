namespace HappyHeadlines.Core.Subscribers;

public enum SubscriberOperation { Subscribe, Unsubscribe, List }

/// <summary>
/// Release toggle for SubscriberService, bound from the <c>FeatureFlags:SubscriberService</c>
/// section. The section lives in a JSON file loaded with reloadOnChange and is read through
/// <c>IOptionsMonitor</c>, so editing the file flips the toggle at runtime - no redeploy, no
/// restart. <see cref="Enabled"/> switches the whole service; the per-operation flags switch it
/// partially (e.g. keep Unsubscribe and List working while Subscribe is off).
/// </summary>
public sealed class SubscriberFeatureFlags
{
    public const string SectionName = "FeatureFlags:SubscriberService";

    public bool Enabled { get; set; } = true;
    public bool Subscribe { get; set; } = true;
    public bool Unsubscribe { get; set; } = true;
    public bool List { get; set; } = true;

    public bool IsEnabled(SubscriberOperation operation) => Enabled && operation switch
    {
        SubscriberOperation.Subscribe => Subscribe,
        SubscriberOperation.Unsubscribe => Unsubscribe,
        SubscriberOperation.List => List,
        _ => false
    };
}

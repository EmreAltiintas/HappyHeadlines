using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Newsletters.Models;

/// <summary>
/// Result of a daily-newsletter run. The actual compose/send flow is stubbed - only counts are
/// returned. <paramref name="SubscribersAvailable"/> is false when SubscriberService was down,
/// toggled off or its circuit was open (fallback: the run still completes, with 0 recipients).
/// </summary>
public record DailyNewsletterResult(
    Continent Continent,
    int ArticleCount,
    int SubscriberCount = 0,
    bool SubscribersAvailable = true);

using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Newsletters.Models;

/// <summary>
/// Result of a daily-newsletter run. The actual compose/send flow is stubbed - only the article
/// count for the continent is returned.
/// </summary>
public record DailyNewsletterResult(Continent Continent, int ArticleCount);

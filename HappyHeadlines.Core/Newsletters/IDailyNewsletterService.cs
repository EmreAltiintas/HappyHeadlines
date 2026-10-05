using HappyHeadlines.Core.Newsletters.Models;
using HappyHeadlines.Db;

namespace HappyHeadlines.Core.Newsletters;

public interface IDailyNewsletterService
{
    Task<DailyNewsletterResult> SendAsync(Continent continent, CancellationToken ct = default);
}

namespace HappyHeadlines.Db;

/// <summary>
/// The continent an article belongs to. Doubles as the Z-axis shard key: every value
/// maps to its own physically separate database. Lives in the Db project because the
/// <see cref="Entities.Article"/> entity depends on it and Core builds on top of Db.
/// </summary>
public enum Continent
{
    Africa = 0,
    Asia = 1,
    Europe = 2,
    NorthAmerica = 3,
    SouthAmerica = 4,
    Oceania = 5,
    Antarctica = 6,
    Global = 7
}

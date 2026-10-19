namespace Mondje.Domain.Constants;

public static class Agencies
{
    public const string Douala = "Douala";
    public const string Yaounde = "Yaoundé";
    public const string Bafoussam = "Bafoussam";

    public static readonly IReadOnlyList<string> All = [Douala, Yaounde, Bafoussam];
}

namespace PixelHelper;

public static class MnemonicPasswordGenerator
{
    private static readonly string[] WordRoots =
    [
        "Solnce", "Barys", "Alatau", "Kofe", "Vesna", "Dala", "Aspan", "Sholpan",
        "Tulpar", "Sunkar", "Zvezda", "Almaty", "Astana", "Bayan", "Falcon", "Silver"
    ];

    private static readonly string[] Suffixes =
    [
        "2026!", "777Kz", "88#", "99$", "2026@", "555!", "101#"
    ];

    public static string GenerateOne() => Generate(1)[0];

    public static List<string> Generate(int count = 3)
    {
        var result = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rand = Random.Shared;

        int attempts = 0;
        while (result.Count < count && attempts < 50)
        {
            attempts++;
            string root = WordRoots[rand.Next(WordRoots.Length)];
            string suffix = Suffixes[rand.Next(Suffixes.Length)];
            string candidate = root + suffix;

            if (candidate.Length >= 8 &&
                PasswordPolicyValidator.Validate(candidate).IsValid &&
                used.Add(candidate))
            {
                result.Add(candidate);
            }
        }

        // Guaranteed fallbacks if random collision
        if (result.Count == 0) result.Add("Barys2026!");
        if (result.Count == 1) result.Add("Solnce777#");
        if (result.Count == 2) result.Add("Alatau88$");

        return result;
    }
}

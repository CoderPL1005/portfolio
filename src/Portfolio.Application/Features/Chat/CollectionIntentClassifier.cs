using System.Globalization;
using System.Text;

namespace Portfolio.Application.Features.Chat;

public static class CollectionIntentClassifier
{
    private const int MaximumClassificationLength = 300;

    private static readonly Category[] Categories =
    [
        new("PROJECT", ["projects", "du an"]),
        new("EXPERIENCE", ["work experience", "employment history", "work history", "kinh nghiem lam viec", "qua trinh lam viec"]),
        new("EDUCATION", ["education", "education history", "academic background", "hoc van", "qua trinh hoc tap"]),
        new("TRAINING", ["training", "training courses", "training programs", "khoa dao tao", "chuong trinh dao tao"]),
        new("CERTIFICATE", ["certificates", "certifications", "chung chi"]),
        new("JOURNEY", ["career timeline", "career journey", "professional journey", "hanh trinh su nghiep", "dong thoi gian su nghiep"])
    ];

    private static readonly string[] CommandMarkers =
    [
        "list", "list all", "show me", "show me all", "name all", "enumerate",
        "liet ke", "ke ten", "danh sach", "tat ca"
    ];

    private static readonly string[] EnglishQuestionMarkers = ["what", "which"];
    private static readonly string[] EnglishCollectionFrames = ["has", "have", "built", "completed", "earned"];
    private static readonly string[] NegationMarkers = ["do not", "don t", "not", "never", "khong", "dung", "chua"];
    private static readonly string[] MetaLanguageMarkers =
    [
        "translate", "translation", "phrase", "sentence", "word", "dich", "cum tu", "cau nay", "tu nay"
    ];

    private static readonly HashSet<string> StandaloneExperienceCollectionFrames =
    [
        "tell me about your work experience",
        "where have you worked",
        "ban da lam o dau",
        "kinh nghiem cua ban la gi"
    ];

    private static readonly string[] ExperienceDetailMarkers =
    [
        "at", "for", "with", "during", "in", "as",
        "tai", "o", "voi", "trong", "nam"
    ];

    public static string? Classify(string message)
    {
        if (string.IsNullOrWhiteSpace(message)
            || message.Length > MaximumClassificationLength
            || message.Contains('\r')
            || message.Contains('\n')
            || message.Contains("://", StringComparison.OrdinalIgnoreCase)
            || message.Contains("www.", StringComparison.OrdinalIgnoreCase)
            || message.IndexOfAny(['"', '`', '\u201c', '\u201d', '\u201e', '\u00ab', '\u00bb']) >= 0)
        {
            return null;
        }

        var normalized = Normalize(message);
        if (normalized.Length == 0
            || ContainsAnyPhrase(normalized, NegationMarkers)
            || ContainsAnyPhrase(normalized, MetaLanguageMarkers))
        {
            return null;
        }

        if (StandaloneExperienceCollectionFrames.Contains(normalized))
        {
            return "EXPERIENCE";
        }

        var matches = Categories
            .Where(category => ContainsAnyPhrase(normalized, category.Aliases))
            .ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        if (matches[0].SourceType == "EXPERIENCE"
            && (normalized.Any(char.IsDigit)
                || normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => ExperienceDetailMarkers.Contains(token, StringComparer.Ordinal))))
        {
            return null;
        }

        var hasCommand = ContainsAnyPhrase(normalized, CommandMarkers);
        var hasEnglishQuestion = HasEnglishQuestionFrame(normalized, matches[0].Aliases);
        var hasVietnameseCollectionFrame = HasVietnameseCollectionFrame(normalized, matches[0].Aliases);

        return hasCommand || hasEnglishQuestion || hasVietnameseCollectionFrame
            ? matches[0].SourceType
            : null;
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Replace('\u0111', 'd').Replace('\u0110', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    private static bool ContainsAnyPhrase(string normalized, IEnumerable<string> phrases)
    {
        var padded = $" {normalized} ";
        return phrases.Any(phrase => padded.Contains($" {phrase} ", StringComparison.Ordinal));
    }

    private static bool HasEnglishQuestionFrame(string normalized, IEnumerable<string> aliases)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var alias in aliases)
        {
            var aliasTokens = alias.Split(' ');
            foreach (var aliasIndex in FindPhraseIndexes(tokens, aliasTokens))
            {
                var questionStart = Math.Max(0, aliasIndex - 4);
                var hasQuestion = tokens[questionStart..aliasIndex]
                    .Any(token => EnglishQuestionMarkers.Contains(token, StringComparer.Ordinal));
                var frameEnd = Math.Min(tokens.Length, aliasIndex + aliasTokens.Length + 6);
                var hasFrame = tokens[(aliasIndex + aliasTokens.Length)..frameEnd]
                    .Any(token => EnglishCollectionFrames.Contains(token, StringComparer.Ordinal));
                if (hasQuestion && hasFrame)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasVietnameseCollectionFrame(string normalized, IEnumerable<string> aliases)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var alias in aliases)
        {
            var aliasTokens = alias.Split(' ');
            foreach (var aliasIndex in FindPhraseIndexes(tokens, aliasTokens))
            {
                var quantifierStart = Math.Max(0, aliasIndex - 2);
                var hasLeadingQuantifier = tokens[quantifierStart..aliasIndex]
                    .Any(token => token is "cac" or "nhung");
                var suffixEnd = Math.Min(tokens.Length, aliasIndex + aliasTokens.Length + 6);
                var suffix = string.Join(' ', tokens[(aliasIndex + aliasTokens.Length)..suffixEnd]);
                var hasQuestionSuffix = ContainsAnyPhrase(suffix, ["nao", "la gi"]);
                if (hasLeadingQuantifier || hasQuestionSuffix)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<int> FindPhraseIndexes(string[] tokens, string[] phrase)
    {
        for (var index = 0; index <= tokens.Length - phrase.Length; index++)
        {
            if (tokens.AsSpan(index, phrase.Length).SequenceEqual(phrase))
            {
                yield return index;
            }
        }
    }

    private sealed record Category(string SourceType, IReadOnlyCollection<string> Aliases);
}

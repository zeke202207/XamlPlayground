using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XamlPlayground.Services.Theming;

internal readonly record struct ResourceReferenceMatch(
    string Kind,
    string Key,
    int Start,
    int Length,
    int KeyStart,
    int KeyLength);

internal static class ResourceReferenceParser
{
    private static readonly string[] ResourceKinds = { "StaticResource", "DynamicResource" };

    public static IEnumerable<ResourceReferenceMatch> Find(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf('{', index);
            if (start < 0)
            {
                yield break;
            }

            if (TryReadAt(text, start, out var match))
            {
                yield return match;
                index = match.Start + Math.Max(match.Length, 1);
            }
            else
            {
                index = start + 1;
            }
        }
    }

    public static bool TryGetExactKey(string value, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var leadingWhitespace = value.Length - value.TrimStart().Length;
        var trimmedLength = value.Trim().Length;
        var match = Find(value).FirstOrDefault(candidate =>
            candidate.Start == leadingWhitespace &&
            candidate.Length == trimmedLength);
        if (match.Length == 0)
        {
            return false;
        }

        key = match.Key;
        return true;
    }

    public static string ReplaceKeys(string text, string oldKey, string newKey)
    {
        var matches = Find(text)
            .Where(match => string.Equals(match.Key, oldKey, StringComparison.Ordinal))
            .OrderByDescending(static match => match.KeyStart)
            .ToArray();
        if (matches.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        foreach (var match in matches)
        {
            builder.Remove(match.KeyStart, match.KeyLength);
            builder.Insert(match.KeyStart, newKey);
        }

        return builder.ToString();
    }

    private static bool TryReadAt(string text, int start, out ResourceReferenceMatch match)
    {
        match = default;
        if (start < 0 || start >= text.Length || text[start] != '{')
        {
            return false;
        }

        var outerEnd = FindBalancedExpressionEnd(text, start, text.Length);
        if (outerEnd < 0)
        {
            return false;
        }

        var index = start + 1;
        SkipWhitespace(text, ref index, outerEnd);

        var kind = ResourceKinds.FirstOrDefault(candidate => StartsWithAt(text, index, candidate));
        if (kind is null)
        {
            return false;
        }

        index += kind.Length;
        if (index < outerEnd && !char.IsWhiteSpace(text[index]))
        {
            return false;
        }

        SkipWhitespace(text, ref index, outerEnd);
        if (StartsWithAt(text, index, "ResourceKey"))
        {
            var nameEnd = index + "ResourceKey".Length;
            var equalsIndex = nameEnd;
            SkipWhitespace(text, ref equalsIndex, outerEnd);
            if (equalsIndex < outerEnd && text[equalsIndex] == '=')
            {
                index = equalsIndex + 1;
                SkipWhitespace(text, ref index, outerEnd);
            }
        }

        if (index >= outerEnd)
        {
            return false;
        }

        var keyStart = index;
        var keyLength = ReadKeyLength(text, keyStart, outerEnd);
        if (keyLength <= 0)
        {
            return false;
        }

        match = new ResourceReferenceMatch(
            kind,
            text.Substring(keyStart, keyLength),
            start,
            outerEnd - start + 1,
            keyStart,
            keyLength);
        return true;
    }

    private static int ReadKeyLength(string text, int keyStart, int outerEnd)
    {
        if (text[keyStart] == '{')
        {
            var nestedEnd = FindBalancedExpressionEnd(text, keyStart, outerEnd + 1);
            return nestedEnd < 0 ? 0 : nestedEnd - keyStart + 1;
        }

        if (text[keyStart] is '"' or '\'')
        {
            var quote = text[keyStart];
            var valueEnd = keyStart + 1;
            while (valueEnd < outerEnd && text[valueEnd] != quote)
            {
                valueEnd++;
            }

            return valueEnd < outerEnd
                ? valueEnd - keyStart + 1
                : valueEnd - keyStart;
        }

        var index = keyStart;
        while (index < outerEnd &&
               !char.IsWhiteSpace(text[index]) &&
               text[index] != ',')
        {
            index++;
        }

        return index - keyStart;
    }

    private static int FindBalancedExpressionEnd(string text, int start, int limit)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = start; index < limit; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static void SkipWhitespace(string text, ref int index, int limit)
    {
        while (index < limit && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    private static bool StartsWithAt(string text, int index, string value)
    {
        return index >= 0 &&
               index + value.Length <= text.Length &&
               string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
    }
}

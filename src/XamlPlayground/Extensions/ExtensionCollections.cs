using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace XamlPlayground.Extensions;

internal static class ExtensionCollections
{
    public static IReadOnlyList<T> CopyList<T>(IEnumerable<T>? values)
    {
        if (values is null)
        {
            return Array.Empty<T>();
        }

        return Array.AsReadOnly(new List<T>(values).ToArray());
    }

    public static IReadOnlyDictionary<TKey, TValue> CopyDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? values)
        where TKey : notnull
    {
        if (values is null)
        {
            return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());
        }

        return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(values));
    }

    public static string RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    public static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

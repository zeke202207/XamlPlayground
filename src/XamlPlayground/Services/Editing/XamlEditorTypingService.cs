using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;

namespace XamlPlayground.Services.Editing;

public static class XamlEditorTypingService
{
    private const int IndentSize = 2;

    public static bool TryHandleTextEntered(
        TextDocument document,
        int caretOffset,
        string text,
        out int newCaretOffset)
    {
        newCaretOffset = caretOffset;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text[^1] switch
        {
            '=' => TryInsertAttributeQuotes(document, caretOffset, out newCaretOffset),
            '>' => TryCompleteCloseTag(document, caretOffset, out newCaretOffset),
            '/' => TryCompleteParentEndTag(document, caretOffset, out newCaretOffset),
            _ => false
        };
    }

    public static bool TryInsertElementBreak(
        TextDocument document,
        int caretOffset,
        out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        var text = document.Text;

        if (caretOffset <= 0 ||
            caretOffset >= text.Length ||
            text[caretOffset - 1] != '>' ||
            text[caretOffset] != '<')
        {
            return false;
        }

        var stack = GetOpenElementStack(text, caretOffset);
        var newline = DetectNewLine(text);
        var innerIndent = new string(' ', stack.Count * IndentSize);
        var nextIsClosingTag = caretOffset + 1 < text.Length && text[caretOffset + 1] == '/';
        var nextIndentLevel = nextIsClosingTag ? Math.Max(0, stack.Count - 1) : stack.Count;
        var nextIndent = new string(' ', nextIndentLevel * IndentSize);

        document.Insert(caretOffset, newline + innerIndent + newline + nextIndent);
        newCaretOffset = caretOffset + newline.Length + innerIndent.Length;
        return true;
    }

    public static bool TrySynchronizeTagRename(
        TextDocument document,
        int editOffset,
        ref int caretOffset)
    {
        var text = document.Text;
        if (!TryFindTagNameAtOrNear(text, editOffset, out var editedTag))
        {
            return false;
        }

        var counterpart = editedTag.IsClosing
            ? FindOpeningTagName(text, editedTag.Start)
            : FindClosingTagName(text, editedTag);

        if (counterpart is null ||
            string.Equals(counterpart.Value.Name, editedTag.Name, StringComparison.Ordinal))
        {
            return false;
        }

        var oldLength = counterpart.Value.NameLength;
        document.Replace(counterpart.Value.NameStart, oldLength, editedTag.Name);

        if (counterpart.Value.NameStart < caretOffset)
        {
            caretOffset += editedTag.Name.Length - oldLength;
        }

        return true;
    }

    private static bool TryInsertAttributeQuotes(
        TextDocument document,
        int caretOffset,
        out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        var text = document.Text;

        if (caretOffset <= 0 ||
            text[caretOffset - 1] != '=' ||
            (caretOffset < text.Length && (text[caretOffset] == '"' || text[caretOffset] == '\'')) ||
            !TryFindActiveTagStart(text, caretOffset - 1, out var tagStart) ||
            !IsNormalOpeningTagStart(text, tagStart))
        {
            return false;
        }

        var index = caretOffset - 2;
        while (index > tagStart && char.IsWhiteSpace(text[index]))
        {
            index--;
        }

        if (index <= tagStart || !IsNameChar(text[index]))
        {
            return false;
        }

        while (index > tagStart && IsNameChar(text[index]))
        {
            index--;
        }

        document.Insert(caretOffset, "\"\"");
        newCaretOffset = caretOffset + 1;
        return true;
    }

    private static bool TryCompleteCloseTag(
        TextDocument document,
        int caretOffset,
        out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        var text = document.Text;

        if (!TryGetTagEndingAt(text, caretOffset, out var tag) ||
            tag.IsClosing ||
            tag.IsSelfClosing ||
            !tag.IsNormal)
        {
            return false;
        }

        var closingTag = $"</{tag.Name}>";
        if (StartsWithOrdinal(text, caretOffset, closingTag))
        {
            return false;
        }

        document.Insert(caretOffset, closingTag);
        return true;
    }

    private static bool TryCompleteParentEndTag(
        TextDocument document,
        int caretOffset,
        out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        var text = document.Text;

        if (caretOffset < 2 ||
            text[caretOffset - 1] != '/' ||
            text[caretOffset - 2] != '<' ||
            (caretOffset < text.Length && (text[caretOffset] == '>' || IsNameChar(text[caretOffset]))))
        {
            return false;
        }

        var parentTagName = GetOpenElementStack(text, caretOffset - 2).LastOrDefault();
        if (string.IsNullOrWhiteSpace(parentTagName))
        {
            return false;
        }

        var insertion = parentTagName + ">";
        document.Insert(caretOffset, insertion);
        newCaretOffset = caretOffset + insertion.Length;
        return true;
    }

    private static bool TryFindTagNameAtOrNear(string text, int offset, out XmlTag tag)
    {
        tag = default;

        if (text.Length == 0)
        {
            return false;
        }

        var searchOffset = Math.Clamp(offset > 0 ? offset - 1 : offset, 0, text.Length - 1);
        var tagStart = -1;
        for (var i = searchOffset; i >= 0; i--)
        {
            if (text[i] == '>')
            {
                return false;
            }

            if (text[i] == '<')
            {
                tagStart = i;
                break;
            }
        }

        if (tagStart < 0 ||
            !TryFindTagEnd(text, tagStart, text.Length, out var tagEnd) ||
            !TryParseTag(text, tagStart, tagEnd, out tag) ||
            !tag.IsNormal ||
            tag.IsSelfClosing)
        {
            return false;
        }

        var nameEnd = tag.NameStart + tag.NameLength;
        return offset >= tag.NameStart && offset <= nameEnd;
    }

    private static XmlTag? FindClosingTagName(string text, XmlTag openingTag)
    {
        if (openingTag.IsClosing || openingTag.IsSelfClosing)
        {
            return null;
        }

        var depth = 0;
        foreach (var tag in EnumerateCompleteTags(text, openingTag.End + 1, text.Length))
        {
            if (!tag.IsNormal)
            {
                continue;
            }

            if (tag.IsClosing)
            {
                if (depth == 0)
                {
                    return tag;
                }

                depth--;
            }
            else if (!tag.IsSelfClosing)
            {
                depth++;
            }
        }

        return null;
    }

    private static XmlTag? FindOpeningTagName(string text, int closingTagStart)
    {
        var tags = EnumerateCompleteTags(text, 0, closingTagStart).ToArray();
        var depth = 0;

        for (var i = tags.Length - 1; i >= 0; i--)
        {
            var tag = tags[i];
            if (!tag.IsNormal)
            {
                continue;
            }

            if (tag.IsClosing)
            {
                depth++;
            }
            else if (!tag.IsSelfClosing)
            {
                if (depth == 0)
                {
                    return tag;
                }

                depth--;
            }
        }

        return null;
    }

    private static List<string> GetOpenElementStack(string text, int endOffset)
    {
        var stack = new List<string>();

        foreach (var tag in EnumerateCompleteTags(text, 0, Math.Clamp(endOffset, 0, text.Length)))
        {
            if (!tag.IsNormal)
            {
                continue;
            }

            if (tag.IsClosing)
            {
                var index = stack.FindLastIndex(name => string.Equals(name, tag.Name, StringComparison.Ordinal));
                if (index >= 0)
                {
                    stack.RemoveRange(index, stack.Count - index);
                }
                else if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            else if (!tag.IsSelfClosing)
            {
                stack.Add(tag.Name);
            }
        }

        return stack;
    }

    private static IEnumerable<XmlTag> EnumerateCompleteTags(string text, int startOffset, int endOffset)
    {
        var index = Math.Clamp(startOffset, 0, text.Length);
        var limit = Math.Clamp(endOffset, 0, text.Length);

        while (index < limit)
        {
            if (text[index] != '<')
            {
                index++;
                continue;
            }

            if (!TryFindTagEnd(text, index, limit, out var tagEnd))
            {
                yield break;
            }

            if (TryParseTag(text, index, tagEnd, out var tag))
            {
                yield return tag;
            }

            index = tagEnd + 1;
        }
    }

    private static bool TryGetTagEndingAt(string text, int endOffset, out XmlTag tag)
    {
        tag = default;

        if (endOffset <= 0 ||
            endOffset > text.Length ||
            text[endOffset - 1] != '>')
        {
            return false;
        }

        XmlTag? lastTag = null;
        foreach (var candidate in EnumerateCompleteTags(text, 0, endOffset))
        {
            lastTag = candidate;
        }

        if (lastTag is not { } value || value.End != endOffset - 1)
        {
            return false;
        }

        tag = value;
        return true;
    }

    private static bool TryFindActiveTagStart(string text, int offset, out int tagStart)
    {
        tagStart = -1;
        var inTag = false;
        var quote = '\0';

        for (var i = 0; i <= offset && i < text.Length; i++)
        {
            var ch = text[i];

            if (!inTag)
            {
                if (ch == '<')
                {
                    inTag = true;
                    tagStart = i;
                }

                continue;
            }

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                inTag = false;
                tagStart = -1;
            }
        }

        return inTag && tagStart >= 0;
    }

    private static bool TryFindTagEnd(string text, int tagStart, int limit, out int tagEnd)
    {
        tagEnd = -1;
        var quote = '\0';

        for (var i = tagStart + 1; i < limit; i++)
        {
            var ch = text[i];

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                tagEnd = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseTag(string text, int start, int end, out XmlTag tag)
    {
        tag = default;

        var index = start + 1;
        while (index < end && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= end)
        {
            return false;
        }

        var marker = text[index];
        var isNormal = marker != '!' && marker != '?';
        var isClosing = false;
        if (marker == '/')
        {
            isClosing = true;
            index++;
            while (index < end && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        var nameStart = index;
        while (index < end && IsNameChar(text[index]))
        {
            index++;
        }

        var nameLength = index - nameStart;
        if (nameLength == 0)
        {
            return false;
        }

        var trim = end - 1;
        while (trim > start && char.IsWhiteSpace(text[trim]))
        {
            trim--;
        }

        tag = new XmlTag(
            start,
            end,
            nameStart,
            nameLength,
            text.Substring(nameStart, nameLength),
            isNormal,
            isClosing,
            !isClosing && trim > start && text[trim] == '/');
        return true;
    }

    private static bool IsNormalOpeningTagStart(string text, int tagStart)
    {
        var index = tagStart + 1;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index < text.Length && text[index] != '/' && text[index] != '!' && text[index] != '?';
    }

    private static bool IsNameChar(char ch)
    {
        return char.IsLetterOrDigit(ch) ||
               ch is '_' or ':' or '.' or '-';
    }

    private static bool StartsWithOrdinal(string text, int offset, string value)
    {
        return offset >= 0 &&
               offset + value.Length <= text.Length &&
               string.CompareOrdinal(text, offset, value, 0, value.Length) == 0;
    }

    private static string DetectNewLine(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private readonly record struct XmlTag(
        int Start,
        int End,
        int NameStart,
        int NameLength,
        string Name,
        bool IsNormal,
        bool IsClosing,
        bool IsSelfClosing);
}

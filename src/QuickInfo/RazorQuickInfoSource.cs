﻿using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using System;
using System.Linq;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.QuickInfo;

internal class RazorQuickInfoSource(ITextBuffer textBuffer, DescriptionGenerator descriptionGenerator, CompletionUtilities completionUtilities) : QuickInfoSource(textBuffer, descriptionGenerator, completionUtilities)
{
    protected override bool IsInClassScope(IAsyncQuickInfoSession session, out SnapshotSpan? span)
    {
        var startPos = new SnapshotPoint(_textBuffer.CurrentSnapshot, 0);
        var searchPos = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);

        if (searchPos == null)
        {
            span = null;
            return false;
        }

        var searchSnapshot = new SnapshotSpan(startPos, searchPos.Value);
        var text = searchSnapshot.GetText();

        var indexOfCurrentClassAttribute = RazorClassScopeHelper.GetLastClassIndex(text);
        if (indexOfCurrentClassAttribute == -1)
        {
            span = null;
            return false;
        }

        var quotationMarkAfterLastClassAttribute = text.IndexOf('\"', indexOfCurrentClassAttribute);
        var lastQuotationMark = text.LastIndexOf('\"');

        if (lastQuotationMark == quotationMarkAfterLastClassAttribute)
        {
            var startIndex = lastQuotationMark + 1;
            text = text.Substring(startIndex);
            startIndex += text.LastIndexOf(' ') == -1 ? 0 : text.LastIndexOf(' ') + 1;
            var length = 1;

            searchSnapshot = new SnapshotSpan(_textBuffer.CurrentSnapshot, startIndex, length);
            var last = searchSnapshot.GetText().Last();

            while (char.IsWhiteSpace(last) == false && last != '"' && last != '\'' && searchSnapshot.End < _textBuffer.CurrentSnapshot.Length)
            {
                length++;
                searchSnapshot = new SnapshotSpan(_textBuffer.CurrentSnapshot, startIndex, length);
                last = searchSnapshot.GetText().Last();
            }

            searchSnapshot = new SnapshotSpan(_textBuffer.CurrentSnapshot, startIndex, length - 1);

            span = searchSnapshot;

            return true;
        }
        else
        {
            var end = searchPos.Value;

            bool isInRazor = false;
            int depth = 0;
            // Number of quotes (excluding \")
            // Odd if in string context, even if not
            int numberOfQuotes = 0;
            bool isEscaping = false;

            char[] endings = ['"', '\''];

            while (end < _textBuffer.CurrentSnapshot.Length - 1 && (depth != 0 || numberOfQuotes % 2 == 1 || endings.Contains(end.GetChar()) == false))
            {
                var character = end.GetChar();

                if (character == '@')
                {
                    isInRazor = true;
                }
                else if (isInRazor)
                {
                    if (searchPos == end)
                    {
                        span = null;
                        return false;
                    }

                    bool escape = isEscaping;
                    isEscaping = false;

                    if (numberOfQuotes % 2 == 1)
                    {
                        if (character == '\\')
                        {
                            isEscaping = true;
                        }
                    }
                    else
                    {
                        if (character == '(')
                        {
                            depth++;
                        }
                        else if (character == ')')
                        {
                            depth--;
                        }
                    }

                    if (character == '"' && !escape)
                    {
                        numberOfQuotes++;
                    }

                    if (depth == 0 && numberOfQuotes % 2 == 0 && char.IsWhiteSpace(character))
                    {
                        isInRazor = false;
                    }
                }
                else if (char.IsWhiteSpace(character) && searchPos <= end)
                {
                    break;
                }

                end += 1;
            }

            if (depth != 0 || numberOfQuotes % 2 == 1 || searchPos > end)
            {
                span = null;
                return false;
            }

            var startIndex = lastQuotationMark + 1;
            text = text.Substring(startIndex);

            // Handle a case where razor code would leave a trailing ) in the beginning
            // of a quick info popup
            if (text.StartsWith(")"))
            {
                int change = 0;
                while (change < text.Length && (text[change] == ')' || char.IsWhiteSpace(text[change])))
                {
                    change++;
                }

                if (change >= text.Length)
                {
                    span = null;
                    return false;
                }

                startIndex += change;
                text = text.Substring(change);
            }

            startIndex += text.LastIndexOf(' ') + 1;

            span = new SnapshotSpan(new SnapshotPoint(_textBuffer.CurrentSnapshot, startIndex), end);
            return true;
        }
    }
}

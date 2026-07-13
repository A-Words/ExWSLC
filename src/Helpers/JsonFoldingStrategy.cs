using ICSharpCode.AvalonEdit.Folding;

namespace ExWSLC.Helpers;

internal static class JsonFoldingStrategy
{
    public static IReadOnlyList<NewFolding> CreateFoldings(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];

        var foldings = new List<NewFolding>();
        var openings = new Stack<OpeningDelimiter>();
        var isInsideString = false;
        var isEscaped = false;
        var lineNumber = 1;

        for (var offset = 0; offset < json.Length; offset++)
        {
            var character = json[offset];

            if (isInsideString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                }
                else if (character == '\\')
                {
                    isEscaped = true;
                }
                else if (character == '"')
                {
                    isInsideString = false;
                }
            }
            else
            {
                switch (character)
                {
                    case '"':
                        isInsideString = true;
                        break;
                    case '{':
                    case '[':
                        openings.Push(new OpeningDelimiter(character, offset, lineNumber));
                        break;
                    case '}':
                    case ']':
                        AddFoldingIfMatched(character, offset, lineNumber, openings, foldings);
                        break;
                }
            }

            if (character == '\n' ||
                (character == '\r' && (offset + 1 >= json.Length || json[offset + 1] != '\n')))
            {
                lineNumber++;
            }
        }

        foldings.Sort((left, right) => left.StartOffset.CompareTo(right.StartOffset));
        return foldings;
    }

    private static void AddFoldingIfMatched(
        char closingCharacter,
        int closingOffset,
        int closingLineNumber,
        Stack<OpeningDelimiter> openings,
        ICollection<NewFolding> foldings)
    {
        if (!openings.TryPeek(out var opening) || !IsMatchingPair(opening.Character, closingCharacter)) return;

        openings.Pop();
        if (opening.LineNumber == closingLineNumber) return;

        foldings.Add(new NewFolding(opening.Offset + 1, closingOffset)
        {
            Name = "…",
            DefaultClosed = false
        });
    }

    private static bool IsMatchingPair(char openingCharacter, char closingCharacter) =>
        openingCharacter == '{' && closingCharacter == '}' ||
        openingCharacter == '[' && closingCharacter == ']';

    private readonly record struct OpeningDelimiter(char Character, int Offset, int LineNumber);
}

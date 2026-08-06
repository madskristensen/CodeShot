using System;

namespace CodeShot.ToolWindows
{
    internal static class TextWrapIndent
    {
        private const int TabSize = 4;

        internal static TextIndent Split(string line)
        {
            line ??= string.Empty;
            var characterCount = 0;
            var columns = 0;

            foreach (var character in line)
            {
                if (character == '\t')
                {
                    columns += TabSize - (columns % TabSize);
                }
                else if (char.IsWhiteSpace(character))
                {
                    columns++;
                }
                else
                {
                    break;
                }

                characterCount++;
            }

            return new TextIndent(characterCount, new string(' ', columns));
        }
    }

    internal readonly struct TextIndent
    {
        internal TextIndent(int characterCount, string whitespace)
        {
            CharacterCount = characterCount;
            Whitespace = whitespace;
        }

        internal int CharacterCount { get; }
        internal string Whitespace { get; }
    }
}

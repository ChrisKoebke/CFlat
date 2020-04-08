using CFlat.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public static unsafe class Tokenizer
    {
        private static Token[] _tokens = new Token[short.MaxValue * 16];
        private static int _index;

        private static string[] _keywords = new string[]
        {
            "root", "2nd", "3rd", "4th", "5th", "6th", "7th", "octave", "8th", "9th", "10th", "11th", "12th", "13th", "14th",
            "cmaj", "dmaj", "emaj", "fmaj", "gmaj", "amaj", "bmaj",
            "cmin", "dmin", "emin", "fmin", "gmin", "amin", "bmin"
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOperator(char i)
        {
            return i == '+' || i == '-' || i == '*' || i == '/' || i == '&' || i == '|' || i == '=' || i == '!' ||
                   i == '<' || i == '>' || i == ':';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifier(char i)
        {
            return char.IsDigit(i) || char.IsLetter(i) || i == '_' || i == '\'';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifierWithDots(char i)
        {
            return IsIdentifier(i) || i == '.';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOpenBracket(char i)
        {
            return i == '(' || i == '{' || i == '[';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCloseBracket(char i)
        {
            return i == ')' || i == '}' || i == ']';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhiteSpace(char i)
        {
            return char.IsWhiteSpace(i) || i == '\r';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNewLine(char i)
        {
            return i == '\n';
        }

        private static string _noteHeads = "𝅝𝅗𝅥𝅘𝅥𝅘𝅥𝅮𝅘𝅥𝅯𝅘𝅥𝅰𝅘𝅥𝅱";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNoteHead(char i)
        {
            for (int j = 0; j < _noteHeads.Length; j++)
            {
                if (_noteHeads[j] == i)
                    return true;
            }

            return false;
        }

        public static TokenStream Tokenize(StringBuilder input, int startIndex, int length, string fileName)
        {
            var result = Tokenize(input, startIndex, length);
            result.FileName = fileName;

            return result;
        }

        public static TokenStream Tokenize(StringBuilder input, int startIndex, int length)
        {
            TokenStream result = default;
            result.Tokens = _tokens;
            result.StartIndex = _index;
            result.Count = 0;

            var endIndex = startIndex + length;
            var lineNumber = 1;

            for (int i = startIndex; i < endIndex; i++)
            {
                if (IsNewLine(input[i]))
                {
                    lineNumber++;
                    continue;
                }

                if (IsWhiteSpace(input[i]))
                    continue;

                if (input[i] == '/' && input[i + 1] == '/')
                {
                    i += 2;
                    while (input[++i] != '\n');
                    lineNumber++;
                    continue;
                }

                if (input[i] == '/' && input[i + 1] == '*')
                {
                    i += 2;
                    while (input[i] != '*' || input[i + 1] != '/')
                    {
                        if (input[i] == '\n') lineNumber++;
                        i++;
                    }
                    i++;

                    continue;
                }

                for (int k = 0; k < _keywords.Length; k++)
                {
                    if (input[i] == _keywords[k][0])
                    {
                        var isEqual = true;
                        for (int j = 0; j < _keywords[k].Length; j++)
                        {
                            if (input[i + j] != _keywords[k][j])
                            {
                                isEqual = false;
                                break;
                            }
                        }

                        if (isEqual)
                        {
                            ref var token = ref result.Tokens[_index++];
                            token.SourceCode = input;
                            token.Index = i;
                            token.LineNumber = lineNumber;
                            token.Length = _keywords[k].Length;
                            token.Type = TokenType.Keyword;

                            i += token.Length;

                            break;
                        }
                    }
                }

                if (IsNoteHead(input[i]))
                {
                    var noteHeadStartIndex = i;
                    while (IsNoteHead(input[++i])) ;

                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = noteHeadStartIndex;
                    token.LineNumber = lineNumber;
                    token.Length = i - noteHeadStartIndex;
                    token.Type = TokenType.NoteHead;

                    i--;
                    result.Count++;

                    continue;
                }

                if (input[i] == '"')
                {
                    var stringStartIndex = i;
                    while (input[++i] != '"') ;

                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = stringStartIndex + 1;
                    token.LineNumber = lineNumber;
                    token.Length = i - stringStartIndex - 1;
                    token.Type = TokenType.String;

                    result.Count++;
                    continue;
                }

                if (IsOperator(input[i]))
                {
                    var operatorStartIndex = i;
                    while (IsOperator(input[++i])) ;

                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = operatorStartIndex;
                    token.LineNumber = lineNumber;
                    token.Length = i - operatorStartIndex;
                    token.Type = TokenType.Operator;

                    i--;
                    result.Count++;

                    continue;
                }

                if (char.IsDigit(input[i]))
                {
                    var digitStartIndex = i;
                    while (char.IsDigit(input[++i])) ;

                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = digitStartIndex;
                    token.LineNumber = lineNumber;
                    token.Length = i - digitStartIndex;
                    token.Type = TokenType.Number;

                    i--;
                    result.Count++;

                    continue;
                }

                if (IsIdentifier(input[i]))
                {
                    var identifierStartIndex = i;
                    while (IsIdentifier(input[++i])) ;

                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = identifierStartIndex;
                    token.LineNumber = lineNumber;
                    token.Length = i - identifierStartIndex;
                    token.Type = TokenType.Identifier;

                    i--;
                    result.Count++;

                    continue;
                }

                if (IsOpenBracket(input[i]))
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.OpenBracket;
                    result.Count++;
                    continue;
                }

                if (IsCloseBracket(input[i]))
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.CloseBracket;
                    result.Count++;
                    continue;
                }

                if (input[i] == ',')
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.Comma;
                    result.Count++;
                    continue;
                }

                if (input[i] == ';')
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.Semicolon;
                    result.Count++;
                    continue;
                }

                if (input[i] == '.')
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.Dot;
                    result.Count++;
                    continue;
                }

                if (input[i] == '?')
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.QuestionMark;
                    result.Count++;
                    continue;
                }

                if (input[i] == '|')
                {
                    ref var token = ref result.Tokens[_index++];
                    token.SourceCode = input;
                    token.Index = i;
                    token.LineNumber = lineNumber;
                    token.Length = 1;
                    token.Type = TokenType.Bar;
                    result.Count++;
                    continue;
                }
            }

            return result;
        }
    }
}

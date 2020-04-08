using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public struct Token : IEquatable<string>
    {
        public StringBuilder SourceCode;
        public string Substitution;

        public int LineNumber;
        public int Index;
        public int Length;

        public TokenType Type;

        public bool IsEmpty => SourceCode == null || Length == 0;

        public bool Equals(string other)
        {
            if (other.Length != Length)
                return false;

            for (int i = 0; i < Length; i++)
            {
                if (other[i] != SourceCode[Index + i])
                    return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (Substitution != null)
                return Substitution;

            if (SourceCode == null)
                return string.Empty;

            char[] buffer = new char[Length];
            SourceCode.CopyTo(Index, buffer, 0, Length);
            return new string(buffer);
        }
    }
}

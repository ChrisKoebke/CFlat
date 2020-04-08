using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public struct TokenStream
    {
        public string FileName;
        public Token[] Tokens;
        public int StartIndex;
        public int Count;
    }
}

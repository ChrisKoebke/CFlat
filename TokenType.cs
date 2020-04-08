using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public enum TokenType
    {
        None,

        Number,
        String,
        Operator,
        NoteHead,

        Identifier,

        OpenBracket,
        CloseBracket,

        Comma,
        Semicolon,
        Dot,
        QuestionMark,
        Bar,

        Keyword
    }
}

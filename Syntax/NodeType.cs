using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public enum NodeType : byte
    {
        Ast,
        Constant,
        Expression,
        Field,
        Include,
        LocalAssignment,
        LocalDeclaration,
        LocalDereference,
        Method,
        MethodCall,
        Note,
        NoteSequence,
        NoteSequenceVariation,
        Operator,
        Placeholder,
        Return,
        Struct
    }
}

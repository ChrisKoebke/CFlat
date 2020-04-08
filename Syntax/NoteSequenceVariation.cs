using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class NoteSequenceVariation : Node
    {
        public override NodeType NodeType => NodeType.NoteSequenceVariation;

        public Token SourceIdentifier;
        public IList<Expression> Expressions;
    }
}

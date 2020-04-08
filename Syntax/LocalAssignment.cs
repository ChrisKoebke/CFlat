using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class LocalAssignment : Node
    {
        public override NodeType NodeType => NodeType.LocalAssignment;

        public Token LocalName;
        public Expression Expression;
    }
}

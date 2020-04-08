using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class LocalDeclaration : Node
    {
        public override NodeType NodeType => NodeType.LocalDeclaration;

        public Token VariableType;
        public Token LocalName;
    }
}

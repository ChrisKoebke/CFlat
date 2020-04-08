using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Struct : Node
    {
        public override NodeType NodeType => NodeType.Struct;

        public Token StructName;
    }
}

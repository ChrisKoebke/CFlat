using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Constant : Node
    {
        public override NodeType NodeType => NodeType.Constant;

        public ConstantType Type;
        public Token Value;
    }

    public enum ConstantType
    {
        Int,
        Float,
        String
    }
}

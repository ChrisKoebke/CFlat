using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class MethodCall : Node
    {
        public override NodeType NodeType => NodeType.MethodCall;

        public Token MethodName;
    }
}

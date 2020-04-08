using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Method : Node
    {
        public override NodeType NodeType => NodeType.Method;

        public Token ReturnTypeName;
        public Token MethodName;

        public IList<Token> ParameterTypeNames;
        public IList<Token> ParameterNames;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Operator : Node
    {
        public override NodeType NodeType => NodeType.Operator;

        public Operator(Token token)
        {
            Token = token;

            if (token.Equals("+"))
            {
                OperatorType = OperatorType.Add;
            }
            else if (token.Equals("-"))
            {
                OperatorType = OperatorType.Subtract;
            }
            else if (token.Equals("*"))
            {
                OperatorType = OperatorType.Multiply;
            }
            else if (token.Equals("/"))
            {
                OperatorType = OperatorType.Divide;
            }
        }

        public Token Token;
        public OperatorType OperatorType;
    }
}

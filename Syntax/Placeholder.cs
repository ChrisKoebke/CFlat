using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Placeholder : Node
    {
        public override NodeType NodeType => NodeType.Placeholder;

        public Token StartToken;
        public float Duration = -1;
        public INode PitchExpression;
    }
}

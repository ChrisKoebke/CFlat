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

        public float rhythm = -1;
        public INode pitchExpression;
    }
}

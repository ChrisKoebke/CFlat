using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Expression : Node
    {
        public override NodeType NodeType => NodeType.Expression;
    }
}

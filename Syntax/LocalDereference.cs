using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class LocalDereference : Node
    {
        public override NodeType NodeType => NodeType.LocalDereference;

        public IList<Token> Path;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Ast : Node
    {
        public Ast() => Root = this;

        public override NodeType NodeType => NodeType.Ast;
        public string FileName;
    }
}

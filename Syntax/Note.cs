using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Note : Node
    {
        public override NodeType NodeType => NodeType.Note;

        public float Duration;
        public Expression Expression;
    }
}

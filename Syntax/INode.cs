using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public interface INode
    {
        NodeType NodeType { get; }
        INode Parent { get; set; }
        IList<INode> Children { get; }
        void Add(INode child);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public abstract class Node : INode
    {
        public Ast Root;
        public INode Parent;
        public List<INode> Children = new List<INode>(128);

        Ast INode.Root
        {
            get { return Root; }
            set { Root = value; }
        }
        INode INode.Parent
        {
            get { return Parent; }
            set { Parent = value; }
        }
        IList<INode> INode.Children => Children;

        public void Add(INode child)
        {
            Children.Add(child);

            child.Root = Root;
            child.Parent = this;
        }

        public abstract NodeType NodeType { get; }
    }
}

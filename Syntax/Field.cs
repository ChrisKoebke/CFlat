using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Syntax
{
    public class Field : Node
    {
        public override NodeType NodeType => NodeType.Field;

        public Token FieldName;
        public Token FieldType;
    }
}

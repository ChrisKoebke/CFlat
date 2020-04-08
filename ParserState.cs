using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public enum ParserState
    {
        None,

        Root,
        Struct,
        Method
    }
}

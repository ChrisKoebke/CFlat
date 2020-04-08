using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public static class BuiltinTypes
    {
        public static Type GetBuiltinType(Token token)
        {
            if (token.Equals("void"))
                return typeof(void);
            else if (token.Equals("bool"))
                return typeof(bool);
            else if (token.Equals("char"))
                return typeof(char);
            else if (token.Equals("f32"))
                return typeof(float);
            else if (token.Equals("f64"))
                return typeof(double);
            else if (token.Equals("i16"))
                return typeof(short);
            else if (token.Equals("i32"))
                return typeof(int);
            else if (token.Equals("i64"))
                return typeof(long);
            else if (token.Equals("string"))
                return typeof(string);

            return null;
        }
    }
}

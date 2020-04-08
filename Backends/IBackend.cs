﻿using CFlat.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Backends
{
    public interface IBackend
    {
        void Compile(Ast ast);
        void Run();
    }
}

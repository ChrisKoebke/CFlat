using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat.Backends
{
    public static class Backend
    {
        public static IBackend Create<T>() where T : IBackend, new()
        {
            var backend = new T();
            backend.Load();

            return backend;
        }
    }
}

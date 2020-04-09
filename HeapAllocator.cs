using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public static unsafe class HeapAllocator
    {
        public static T* Allocate<T>(int count) where T : unmanaged
        {
            return (T*)Marshal.AllocHGlobal(count * sizeof(T));
        }

        public static void Free(void* pointer)
        {
            Marshal.FreeHGlobal((IntPtr)pointer);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public static unsafe class ArenaAllocator
    {
        public const int TotalSize = 128 * 1024 * 1024;

        public static byte* Memory;
        public static int Position = 0;

        public static void Init()
        {
            Memory = HeapAllocator.Allocate<byte>(TotalSize);
        }

        public static void Free()
        {
            Position = 0;
        }

        public static T* Allocate<T>(int count) where T : unmanaged
        {
            var result = (T*)(Memory + Position);
            var offset = count * sizeof(T);

            if ((Position += offset) > TotalSize)
            {
                throw new OutOfMemoryException("Runtime is out of memory.");
            }

            return result;
        }
    }
}

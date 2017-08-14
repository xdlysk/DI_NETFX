using System;

namespace DI.Extensions
{
    public static class ArrayExtensions
    {
        public static T[] Empty<T>()
        {
            return new T[0];
        }
    }
}

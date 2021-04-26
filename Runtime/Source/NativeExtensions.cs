using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using System;

namespace Landscape.RuntimeVirtualTexture
{
    static class NativeExtensions
    {
        public static int ToInt(this Color32 color)
        {
            return (int)color.r + ((int)color.g << 8) + ((int)color.b << 16) + ((int)color.a << 24);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(int) })]
        internal struct DefaultComparer<T> : IEqualityComparer<T> where T : IEquatable<T>
        {
            public bool Equals(T x, T y) => x.Equals(y);

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(int), typeof(DefaultComparer<int>) })]
        public static unsafe int Unique<T, U>(T* array, int size, U comp) where T : unmanaged where U : IEqualityComparer<T>
        {
            int length = 0, current = 1;
            while (current < size)
            {
                if (!comp.Equals(array[current], array[length]))
                {
                    length += 1;
                    if (current != length)
                        array[length] = array[current];
                }
                current += 1;
            }
            return length;
        }

        public static unsafe int Unique<T>(T* array, int size) where T : unmanaged, IEquatable<T>
        {
            return Unique(array, size, new DefaultComparer<T>());
        }

        public static unsafe NativeArray<T> Unique<T>(NativeArray<T> array) where T : unmanaged, IEquatable<T>
        {
            return array.GetSubArray(0, Unique((T*)array.GetUnsafePtr(), array.Length, new DefaultComparer<T>()));
        }
    }

    [BurstCompatible]
    struct ColorComparer : IComparer<Color32>
    {
        public int Compare(Color32 x, Color32 y) => x.ToInt().CompareTo(y.ToInt());
    }

    [BurstCompatible]
    struct ColorEqualComparer : IEqualityComparer<Color32>
    {
        public bool Equals(Color32 x, Color32 y) => x.ToInt() == y.ToInt();
        public int GetHashCode(Color32 obj) => obj.GetHashCode();
    }
}

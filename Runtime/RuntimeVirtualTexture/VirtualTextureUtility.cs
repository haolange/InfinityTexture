using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Globalization;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
    public struct FRect : IEquatable<FRect>, IFormattable
    {
        private float m_XMin;
        private float m_YMin;
        private float m_Width;
        private float m_Height;

        public FRect(float x, float y, float width, float height)
        {
            this.m_XMin = x;
            this.m_YMin = y;
            this.m_Width = width;
            this.m_Height = height;
        }

        public FRect(float2 position, float2 size)
        {
            this.m_XMin = position.x;
            this.m_YMin = position.y;
            this.m_Width = size.x;
            this.m_Height = size.y;
        }

        public FRect(FRect source)
        {
            this.m_XMin = source.m_XMin;
            this.m_YMin = source.m_YMin;
            this.m_Width = source.m_Width;
            this.m_Height = source.m_Height;
        }

        public static FRect zero => new FRect(0.0f, 0.0f, 0.0f, 0.0f);
        public static FRect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => new FRect(xmin, ymin, xmax - xmin, ymax - ymin);

        public void Set(float x, float y, float width, float height)
        {
            this.m_XMin = x;
            this.m_YMin = y;
            this.m_Width = width;
            this.m_Height = height;
        }

        public float x
        {
            get => this.m_XMin;
            set => this.m_XMin = value;
        }

        public float y
        {
            get => this.m_YMin;
            set => this.m_YMin = value;
        }

        public float2 position
        {
            get => new float2(this.m_XMin, this.m_YMin);
            set
            {
                this.m_XMin = value.x;
                this.m_YMin = value.y;
            }
        }

        public float2 center
        {
            get => new float2(this.x + this.m_Width / 2f, this.y + this.m_Height / 2f);
            set
            {
                this.m_XMin = value.x - this.m_Width / 2f;
                this.m_YMin = value.y - this.m_Height / 2f;
            }
        }

        public float2 min
        {
            get => new float2(this.xMin, this.yMin);
            set
            {
                this.xMin = value.x;
                this.yMin = value.y;
            }
        }

        public float2 max
        {
            get => new float2(this.xMax, this.yMax);
            set
            {
                this.xMax = value.x;
                this.yMax = value.y;
            }
        }

        public float width
        {
            get => this.m_Width;
            set => this.m_Width = value;
        }

        public float height
        {
            get => this.m_Height;
            set => this.m_Height = value;
        }

        public float2 size
        {
            get => new float2(this.m_Width, this.m_Height);
            set
            {
                this.m_Width = value.x;
                this.m_Height = value.y;
            }
        }

        public float xMin
        {
            get => this.m_XMin;
            set
            {
                float xMax = this.xMax;
                this.m_XMin = value;
                this.m_Width = xMax - this.m_XMin;
            }
        }

        public float yMin
        {
            get => this.m_YMin;
            set
            {
                float yMax = this.yMax;
                this.m_YMin = value;
                this.m_Height = yMax - this.m_YMin;
            }
        }

        public float xMax
        {
            get => this.m_Width + this.m_XMin;
            set => this.m_Width = value - this.m_XMin;
        }
        public float yMax
        {
            get => this.m_Height + this.m_YMin;
            set => this.m_Height = value - this.m_YMin;
        }

        public bool Contains(float2 point) => (double)point.x >= (double)this.xMin && (double)point.x < (double)this.xMax && (double)point.y >= (double)this.yMin && (double)point.y < (double)this.yMax;

        public bool Contains(float3 point) => (double)point.x >= (double)this.xMin && (double)point.x < (double)this.xMax && (double)point.y >= (double)this.yMin && (double)point.y < (double)this.yMax;

        public bool Contains(float3 point, bool allowInverse) => !allowInverse ? this.Contains(point) : ((double)this.width < 0.0 && (double)point.x <= (double)this.xMin && (double)point.x > (double)this.xMax || (double)this.width >= 0.0 && (double)point.x >= (double)this.xMin && (double)point.x < (double)this.xMax) & ((double)this.height < 0.0 && (double)point.y <= (double)this.yMin && (double)point.y > (double)this.yMax || (double)this.height >= 0.0 && (double)point.y >= (double)this.yMin && (double)point.y < (double)this.yMax);

        private static FRect OrderMinMax(FRect rect)
        {
            if ((double)rect.xMin > (double)rect.xMax)
            {
                float xMin = rect.xMin;
                rect.xMin = rect.xMax;
                rect.xMax = xMin;
            }
            if ((double)rect.yMin > (double)rect.yMax)
            {
                float yMin = rect.yMin;
                rect.yMin = rect.yMax;
                rect.yMax = yMin;
            }
            return rect;
        }

        public bool Overlaps(FRect other) => (double)other.xMax > (double)this.xMin && (double)other.xMin < (double)this.xMax && (double)other.yMax > (double)this.yMin && (double)other.yMin < (double)this.yMax;

        public bool Overlaps(FRect other, bool allowInverse)
        {
            FRect rect = this;
            if (allowInverse)
            {
                rect = FRect.OrderMinMax(rect);
                other = FRect.OrderMinMax(other);
            }
            return rect.Overlaps(other);
        }

        public static float2 NormalizedToPoint(FRect rectangle, float2 normalizedRectCoordinates)
        {
            return new float2(math.lerp(rectangle.x, rectangle.xMax, normalizedRectCoordinates.x), math.lerp(rectangle.y, rectangle.yMax, normalizedRectCoordinates.y));
        }

        public static float2 PointToNormalized(FRect rectangle, float2 point) => new float2(Mathf.InverseLerp(rectangle.x, rectangle.xMax, point.x), Mathf.InverseLerp(rectangle.y, rectangle.yMax, point.y));

        public static bool operator !=(FRect lhs, FRect rhs) => !(lhs == rhs);

        public static bool operator ==(FRect lhs, FRect rhs) => (double)lhs.x == (double)rhs.x && (double)lhs.y == (double)rhs.y && (double)lhs.width == (double)rhs.width && (double)lhs.height == (double)rhs.height;

        public override int GetHashCode()
        {
            float num1 = this.x;
            int hashCode = num1.GetHashCode();
            num1 = this.width;
            int num2 = num1.GetHashCode() << 2;
            int num3 = hashCode ^ num2;
            num1 = this.y;
            int num4 = num1.GetHashCode() >> 2;
            int num5 = num3 ^ num4;
            num1 = this.height;
            int num6 = num1.GetHashCode() >> 1;
            return num5 ^ num6;
        }

        public override bool Equals(object other) => other is FRect other1 && this.Equals(other1);

        public bool Equals(FRect other)
        {
            int num1;
            if (this.x.Equals(other.x))
            {
                float num2 = this.y;
                if (num2.Equals(other.y))
                {
                    num2 = this.width;
                    if (num2.Equals(other.width))
                    {
                        num2 = this.height;
                        num1 = num2.Equals(other.height) ? 1 : 0;
                        goto label_5;
                    }
                }
            }
            num1 = 0;

        label_5:
            return num1 != 0;
        }

        public override string ToString() => this.ToString((string)null, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);

        public string ToString(string format) => this.ToString(format, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
                format = "F2";
            object[] objArray = new object[4];
            float num = this.x;
            objArray[0] = (object)num.ToString(format, formatProvider);
            num = this.y;
            objArray[1] = (object)num.ToString(format, formatProvider);
            num = this.width;
            objArray[2] = (object)num.ToString(format, formatProvider);
            num = this.height;
            objArray[3] = (object)num.ToString(format, formatProvider);
            return string.Format("(x:{0}, y:{1}, width:{2}, height:{3})", objArray);
        }
    }

    public struct FRectInt : IEquatable<FRectInt>, IFormattable
    {
        private int m_XMin;
        private int m_YMin;
        private int m_Width;
        private int m_Height;

        public int x
        {
            get => this.m_XMin;
            set => this.m_XMin = value;
        }

        public int y
        {
            get => this.m_YMin;
            set => this.m_YMin = value;
        }

        public float2 center => new float2((float)this.x + (float)this.m_Width / 2f, (float)this.y + (float)this.m_Height / 2f);

        public int2 min
        {
            get => new int2(this.xMin, this.yMin);
            set
            {
                this.xMin = value.x;
                this.yMin = value.y;
            }
        }

        public int2 max
        {
            get => new int2(this.xMax, this.yMax);
            set
            {
                this.xMax = value.x;
                this.yMax = value.y;
            }
        }

        public int width
        {
            get => this.m_Width;
            set => this.m_Width = value;
        }

        public int height
        {
            get => this.m_Height;
            set => this.m_Height = value;
        }

        public int xMin
        {
            get => math.min(this.m_XMin, this.m_XMin + this.m_Width);
            set
            {
                int xMax = this.xMax;
                this.m_XMin = value;
                this.m_Width = xMax - this.m_XMin;
            }
        }

        public int yMin
        {
            get => math.min(this.m_YMin, this.m_YMin + this.m_Height);
            set
            {
                int yMax = this.yMax;
                this.m_YMin = value;
                this.m_Height = yMax - this.m_YMin;
            }
        }

        public int xMax
        {
            get => math.max(this.m_XMin, this.m_XMin + this.m_Width);
            set => this.m_Width = value - this.m_XMin;
        }

        public int yMax
        {
            get => math.max(this.m_YMin, this.m_YMin + this.m_Height);
            set => this.m_Height = value - this.m_YMin;
        }

        public int2 position
        {
            get => new int2(this.m_XMin, this.m_YMin);
            set
            {
                this.m_XMin = value.x;
                this.m_YMin = value.y;
            }
        }

        public int2 size
        {
            get => new int2(this.m_Width, this.m_Height);
            set
            {
                this.m_Width = value.x;
                this.m_Height = value.y;
            }
        }

        public void SetMinMax(int2 minPosition, int2 maxPosition)
        {
            this.min = minPosition;
            this.max = maxPosition;
        }

        public FRectInt(int xMin, int yMin, int width, int height)
        {
            this.m_XMin = xMin;
            this.m_YMin = yMin;
            this.m_Width = width;
            this.m_Height = height;
        }

        public FRectInt(int2 position, int2 size)
        {
            this.m_XMin = position.x;
            this.m_YMin = position.y;
            this.m_Width = size.x;
            this.m_Height = size.y;
        }

        public void ClampToBounds(FRectInt bounds)
        {
            this.position = new int2(Math.Max(Math.Min(bounds.xMax, this.position.x), bounds.xMin), Math.Max(Math.Min(bounds.yMax, this.position.y), bounds.yMin));
            this.size = new int2(Math.Min(bounds.xMax - this.position.x, this.size.x), Math.Min(bounds.yMax - this.position.y, this.size.y));
        }

        public bool Contains(int2 position) => position.x >= this.xMin && position.y >= this.yMin && position.x < this.xMax && position.y < this.yMax;

        public bool Overlaps(FRectInt other) => other.xMin < this.xMax && other.xMax > this.xMin && other.yMin < this.yMax && other.yMax > this.yMin;

        public override string ToString() => this.ToString((string)null, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);

        public string ToString(string format) => this.ToString(format, (IFormatProvider)CultureInfo.InvariantCulture.NumberFormat);

        public string ToString(string format, IFormatProvider formatProvider) => string.Format("(x:{0}, y:{1}, width:{2}, height:{3})", (object)this.x.ToString(format, formatProvider), (object)this.y.ToString(format, formatProvider), (object)this.width.ToString(format, formatProvider), (object)this.height.ToString(format, formatProvider));

        public bool Equals(FRectInt other) => this.m_XMin == other.m_XMin && this.m_YMin == other.m_YMin && this.m_Width == other.m_Width && this.m_Height == other.m_Height;
    }

    internal static class FVirtualTextureUtility
    {
        public static Mesh BuildQuadMesh()
        {
            List<Vector3> VertexArray = new List<Vector3>();
            List<int> IndexArray = new List<int>();
            List<Vector2> UB0Array = new List<Vector2>();

            VertexArray.Add(new Vector3(0, 1, 0.1f));
            VertexArray.Add(new Vector3(0, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 0, 0.1f));
            VertexArray.Add(new Vector3(1, 1, 0.1f));

            UB0Array.Add(new Vector2(0, 1));
            UB0Array.Add(new Vector2(0, 0));
            UB0Array.Add(new Vector2(1, 0));
            UB0Array.Add(new Vector2(1, 1));

            IndexArray.Add(0);
            IndexArray.Add(1);
            IndexArray.Add(2);
            IndexArray.Add(2);
            IndexArray.Add(3);
            IndexArray.Add(0);

            Mesh mesh = new Mesh();
            mesh.SetVertices(VertexArray);
            mesh.SetUVs(0, UB0Array);
            mesh.SetTriangles(IndexArray, 0);
            return mesh;
        }

        public static void AllocateRquestInfo(in int x, in int y, in int mip, ref FPageRequestInfo pageRequest, in NativeList<FPageRequestInfo> pageRequests)
        {
            for (int i = 0; i < pageRequests.Length; ++i)
            {
                pageRequest = pageRequests[i];
                if (pageRequest.pageX == x && pageRequest.pageY == y && pageRequest.mipLevel == mip)
                {
                    pageRequest = new FPageRequestInfo(x, y, mip, true);
                    return;
                }
            }

            pageRequest = new FPageRequestInfo(x, y, mip);
            pageRequests.Add(pageRequest);
            return;
        }

        public static void LoadPage(in int x, in int y, ref FPage page, in NativeList<FPageRequestInfo> pageRequests)
        {
            if (page.isNull == true) { return; }
            if (page.payload.pageRequestInfo.isNull == false) { return; }
            AllocateRquestInfo(x, y, page.mipLevel, ref page.payload.pageRequestInfo, pageRequests);
        }

        public static void ActivatePage(in int x, in int y, in int mip, in int maxMip, in int frameCount, in int tileNum, in int pageSize, ref FLruCache lruCache, in NativeArray<FPageTable> pageTables, in NativeList<FPageRequestInfo> pageRequests)
        {
            if (mip > maxMip || mip < 0 || x < 0 || y < 0 || x >= pageSize || y >= pageSize) { return; }

            ref FPage page = ref pageTables[mip].GetPage(x, y);
            if (page.isNull == true) { return; }

            if (!page.payload.isReady)
            {
                LoadPage(x, y, ref page, pageRequests);
            }

            if (page.payload.isReady)
            {
                page.payload.activeFrame = frameCount;
                lruCache.SetActive(page.payload.pageCoord.y * tileNum + page.payload.pageCoord.x);
                return;
            }

            return;
        }
    }
}

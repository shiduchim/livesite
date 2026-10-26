namespace Offline3D.RevitExporter;

/// <summary>Hashing for packed 64-bit keys. .NET's default for a long/ulong is
/// low ^ high, which puts keys built from two neighbouring numbers (edge
/// vertex pairs, element/material pairs) into a handful of buckets.</summary>
internal sealed class PackedKeyComparer : IEqualityComparer<ulong>, IEqualityComparer<long>
{
    public static readonly PackedKeyComparer Instance = new PackedKeyComparer();
    public bool Equals(ulong a, ulong b) => a == b;
    public int GetHashCode(ulong key) => Mix(0, key);
    public bool Equals(long a, long b) => a == b;
    public int GetHashCode(long key) => Mix(0, (ulong)key);

    public static int Mix(int seed, ulong value)
    {
        unchecked
        {
            ulong h = (value ^ (ulong)(uint)seed * 0xC2B2AE3D27D4EB4FUL) * 0x9E3779B97F4A7C15UL;
            return (int)(h >> 32) ^ (int)h;
        }
    }
}

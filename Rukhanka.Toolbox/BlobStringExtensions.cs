using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Hash128 = Unity.Entities.Hash128;
using FixedStringName = Unity.Collections.FixedString512Bytes;

////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Toolbox
{
public static class BlobStringExtensions
{
	public unsafe static Hash128 CalculateHash128(ref this BlobString s)
	{
		var hasher = new xxHash3.StreamingState();
		//	BlobString internally is just BlobArray, so do a little C++ magic here and reinterpret BlobString as BlobArray (with hope that first member of former will remain as its data)
		ref var stringAsArr = ref UnsafeUtility.As<BlobString, BlobArray<byte>>(ref s);
		//	Ignoring trailing zero byte
		hasher.Update(stringAsArr.GetUnsafePtr(), stringAsArr.Length - 1);
		var rv = new Hash128(hasher.DigestHash128());
		return rv;
	}

////////////////////////////////////////////////////////////////////////////////////

	public static FixedStringName ToFixedString(ref this BlobString s)
	{
		var rv = new FixedStringName();
		if (s.Length > 0)
			s.CopyTo(ref rv);
		return rv;
	}

////////////////////////////////////////////////////////////////////////////////////

	//	Truncate string if needed
	public static unsafe ConversionError CopyToWithTruncate<T>(ref this BlobString bs, ref T dest) where T : INativeList<byte>
	{
		fixed (BlobString* blobStringPtr = &bs)
		{
			var ba = (BlobArray<byte>*)blobStringPtr;
			byte* srcBuffer = (byte*)ba->GetUnsafePtr();
			int srcLength = bs.Length;
			dest.Length = math.min(srcLength, dest.Capacity);
			byte* destBuffer = (byte*)UnsafeUtility.AddressOf(ref dest.ElementAt(0));
			int destCapacity = dest.Capacity;
			var err = Unicode.Utf8ToUtf8(srcBuffer, srcLength, destBuffer, out var destLength, destCapacity);
			dest.Length = destLength;
			return err;
		}
	}
}
}


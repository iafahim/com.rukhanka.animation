
using Unity.Collections;
using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public struct InternalAnimatorDataSingleton: IComponentData
{
    internal NativeParallelHashMap<int, BlobAssetReference<ControllerAnimationsBlob>> animatorOverrideAnimationsMap;

/////////////////////////////////////////////////////////////////////////////////

	public static InternalAnimatorDataSingleton MakeDefault()
	{
		var rv = new InternalAnimatorDataSingleton()
		{
			animatorOverrideAnimationsMap = new (0xff, Allocator.Persistent)
		};
		return rv;
	}

/////////////////////////////////////////////////////////////////////////////////

	public void Dispose()
	{
		foreach (var kv in animatorOverrideAnimationsMap)
			kv.Value.Dispose();
		animatorOverrideAnimationsMap.Dispose();
	}
}
}

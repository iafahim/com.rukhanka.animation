using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{
public struct RigDefinitionComponent: IComponentData, IEnableableComponent
{
	public BlobAssetReference<RigDefinitionBlob> rigBlob;
	public bool applyRootMotion;
	
	//	Dynamic per-frame data
	internal DynamicFrameData dynamicFrameData;
}

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

public struct DynamicFrameData
{
	public int bonePoseOffset;
	public int boneFlagsOffset;
	public int rigBoneCount;
	
	public static DynamicFrameData MakeInvalid()
	{
		return new DynamicFrameData()
		{
			boneFlagsOffset = -1,
			bonePoseOffset = -1,
			rigBoneCount = -1,
		};
	}
}
}


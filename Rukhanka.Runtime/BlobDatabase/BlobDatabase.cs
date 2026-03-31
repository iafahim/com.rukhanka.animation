using System;
using Unity.Collections;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

[assembly: RegisterGenericComponentType(typeof(Rukhanka.NewBlobAssetDatabaseRecord<Rukhanka.AnimationClipBlob>))]
[assembly: RegisterGenericComponentType(typeof(Rukhanka.NewBlobAssetDatabaseRecord<Rukhanka.AvatarMaskBlob>))]

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka
{

public struct BlobDatabaseSingleton: IComponentData
{
    public NativeHashMap<Hash128, BlobAssetReference<AnimationClipBlob>> animations;
    public NativeHashMap<Hash128, BlobAssetReference<AvatarMaskBlob>> avatarMasks;
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static BlobAssetReference<T> GetBlobAsset<T>(Hash128 blobHash, NativeHashMap<Hash128, BlobAssetReference<T>> blobDatabase) where T: unmanaged
    {
        if (!blobDatabase.TryGetValue(blobHash, out var bar))
            return default;
        
        return bar;
    }
    
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public BlobAssetReference<AvatarMaskBlob> GetAvatarMaskBlob(Hash128 blobHash) => GetBlobAsset(blobHash, avatarMasks);
    public BlobAssetReference<AnimationClipBlob> GetAnimationClipBlob(Hash128 blobHash) => GetBlobAsset(blobHash, animations);
}

//=================================================================================================================//

public struct NewBlobAssetDatabaseRecord<T>: IBufferElementData where T: unmanaged
{
    public Hash128 hash;
    public BlobAssetReference<T> value;
}

}


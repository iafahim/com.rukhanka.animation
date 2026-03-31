using Unity.Entities;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Toolbox
{
public static class EntityTools
{
	public static unsafe bool TryGetChunkComponentData<T>(ArchetypeChunk chunk, Entity otherChunkEntity, ref ComponentTypeHandle<T> typeHandle, out T outChunkData) where T : unmanaged, IComponentData
    {
	    outChunkData = default;
		var otherChunkIndex = chunk.m_EntityComponentStore->GetChunk(otherChunkEntity);
		var otherChunk = new ArchetypeChunk(otherChunkIndex, chunk.m_EntityComponentStore);
		if (!otherChunk.HasChunkComponent<T>())
		{
			return false;
		}
		outChunkData = otherChunk.GetChunkComponentData(ref typeHandle);
		return true;
	}

}
}

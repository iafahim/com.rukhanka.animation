#ifndef SKINNDE_MESH_SAMPLER_HLSL_
#define SKINNDE_MESH_SAMPLER_HLSL_

#include "Packages/com.rukhanka.animation/Rukhanka.Runtime/Deformation/Resources/ComputeDeformedVertex.hlsl"

///////////////////////////////////////////////////////////////////////////////////////////

float3 GetRandomSkinnedVertex(in uint vertexID, in int deformedMeshIndex, in float3 vertexDefaultPosition)
{
    DeformedVertex v = (DeformedVertex)0;
    v.position = vertexDefaultPosition;
    v = GetDeformedVertexForMesh(vertexID, v, deformedMeshIndex);
    return v.position;
}

#endif

#pragma once

/////////////////////////////////////////////////////////////////////////////////

#include "Packages/com.rukhanka.animation/Rukhanka.Runtime/Common/Shaders/ShaderConf.hlsl"
#include "Packages/com.rukhanka.animation/Rukhanka.Runtime/Common/Shaders/Debug.hlsl"
#include "Packages/com.rukhanka.animation/Rukhanka.Runtime/Deformation/Resources/DeformationCommon.hlsl"
#ifdef RUKHANKA_INPLACE_SKINNING
#include "Packages/com.rukhanka.animation/Rukhanka.Runtime/Deformation/Resources/Skinning.hlsl"
#endif

#ifdef RUKHANKA_HALF_DEFORMED_DATA
StructuredBuffer<PackedDeformedVertex> _DeformedMeshData;
#else
StructuredBuffer<DeformedVertex> _DeformedMeshData;
#endif

#undef _DeformedMeshIndex
#undef _DeformationParamsForMotionVectors

/////////////////////////////////////////////////////////////////////////////////

DeformedVertex GetDeformedVertexForMesh(uint vertexID, DeformedVertex originalVertex, uint vertexOffsetOrMeshIndex)
{
#ifndef RUKHANKA_INPLACE_SKINNING
    
    uint vertexOffsetForMesh = vertexOffsetOrMeshIndex;
#ifdef RUKHANKA_HALF_DEFORMED_DATA
    PackedDeformedVertex vertexData = _DeformedMeshData[vertexOffsetForMesh + absoluteVertexIndex];
    DeformedVertex rv = vertexData.Unpack();
#else
    DeformedVertex rv = _DeformedMeshData[vertexOffsetForMesh + vertexID];
#endif  // RUKHANKA_HALF_DEFORMED_DATA

//-------- Inplace skinning code path -------------//
#else

    DeformedVertex rv = originalVertex;

    uint meshIndex = vertexOffsetOrMeshIndex;
    if (meshIndex != 0xffffffff)
    {
        MeshFrameDeformationDescription mfd = frameDeformedMeshes[meshIndex];

        uint absoluteInputMeshVertexIndex = vertexID + mfd.baseInputMeshVertexIndex;
        SourceSkinnedMeshVertex smv = SourceSkinnedMeshVertex::ReadFromRawBuffer(inputMeshVertexData, absoluteInputMeshVertexIndex);

        rv = ApplyBlendShapes(rv, vertexID, mfd);
        rv = ApplySkinMatrices(rv, smv.boneWeightsOffset, smv.boneWeightsCount, mfd);
    }
    else
    {
        rv = (DeformedVertex)0;
    }

#endif 
    return rv;
}

/////////////////////////////////////////////////////////////////////////////////

void ComputeDeformedVertex_float(in uint vertexID, in float3 vertex, in float3 normal, in float3 tangent, out float3 deformedVertex, out float3 deformedNormal, out float3 deformedTangent)
{
    deformedVertex = vertex;
    deformedNormal = normal;
    deformedTangent = tangent;

#ifdef DOTS_INSTANCING_ON

#ifdef RUKHANKA_ENABLE_DEFORMATION_MOTION_VECTORS
    const uint4 materialProperty = asuint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float4, _DeformationParamsForMotionVectors));
    const uint currentFrameIndex = materialProperty[2];
    const uint index = materialProperty[currentFrameIndex];
#else
    const uint index = asuint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _DeformedMeshIndex));
#endif  // RUKHANKA_ENABLE_DEFORMATION_MOTION_VECTORS
    
    DeformedVertex v;
    v.position = deformedVertex;
    v.normal = deformedNormal;
    v.tangent = deformedTangent;
    
    v = GetDeformedVertexForMesh(vertexID, v, index);
    
    deformedVertex = v.position;
    deformedNormal = v.normal;
    deformedTangent = v.tangent;
    
#endif // DOTS_INSTANCING_ON
}

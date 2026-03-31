#pragma once

///////////////////////////////////////////////////////////////////////////////////////////

#ifdef DOTS_INSTANCING_ON
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float, _DeformedMeshIndex)
    UNITY_DOTS_INSTANCED_PROP(float4, _DeformationParamsForMotionVectors)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#endif


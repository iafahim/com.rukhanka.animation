#ifndef SKIN_MATRIX_HLSL_
#define SKIN_MATRIX_HLSL_

/////////////////////////////////////////////////////////////////////////////////

struct SkinMatrix
{
    static const uint size = 4 * 3 * 4;

/////////////////////////////////////////////////////////////////////////////////

    static float3x4 Identity()
    {
        float3x4 rv = float3x4
        (
            float3(1, 0, 0),
            float3(0, 1, 0),
            float3(0, 0, 1),
            float3(0, 0, 0)
        );
        return rv;
    }

/////////////////////////////////////////////////////////////////////////////////

    static float3x4 ReadFromRawBuffer(ByteAddressBuffer b, int index)
    {
        uint byteAddress = index * size;
       
        float3x4 rv;

        rv._11_21_31_12 = asfloat(b.Load4(byteAddress + 00));
        rv._22_32_13_23 = asfloat(b.Load4(byteAddress + 16));
        rv._33_14_24_34 = asfloat(b.Load4(byteAddress + 32));

        CHECK_RAW_BUFFER_OUT_OF_BOUNDS(RUKHANKADEBUGMARKERS_DEFORMATION_FRAME_SKIN_MATRICES_READ, byteAddress, size, b);
        
        return rv;
    }
    
/////////////////////////////////////////////////////////////////////////////////

    static void WriteToRawBuffer(RWByteAddressBuffer b, float3x4 v, int index)
    {
        uint byteAddress = index * size;
       
        b.Store4(byteAddress + 00, asuint(v._11_21_31_12));
        b.Store4(byteAddress + 16, asuint(v._22_32_13_23));
        b.Store4(byteAddress + 32, asuint(v._33_14_24_34));
        
        CHECK_RAW_BUFFER_OUT_OF_BOUNDS(RUKHANKADEBUGMARKERS_GPUANIMATOR_COMPUTE_SKIN_MATRICES_OUT_SKIN_MATRICES_WRITE, byteAddress, size, b);
    }
};

/////////////////////////////////////////////////////////////////////////////////

#endif



#ifndef TRACK_SAMPLER_HLSL_
#define TRACK_SAMPLER_HLSL_

/////////////////////////////////////////////////////////////////////////////////

#define TRACK_SAMPLER_TYPE_DEFAULT 0
#define TRACK_SAMPLER_TYPE_FIRST_FRAME 1
#define TRACK_SAMPLER_TYPE_LAST_FRAME 2

/////////////////////////////////////////////////////////////////////////////////

struct TrackSampler
{
    float time;
    int samplerType;

    float Sample(Track tk, int keyFrameBaseAddress)
    {
        //  With absence of interfaces in DXC and templates (hello HLSL 2021) need to invent such apprach
        switch (samplerType)
        {
        case TRACK_SAMPLER_TYPE_DEFAULT:
            return tk.SampleByBinarySearch(time, keyFrameBaseAddress);
            break;
        case TRACK_SAMPLER_TYPE_FIRST_FRAME:
            return tk.GetFirstFrameValue(keyFrameBaseAddress);
            break;
        case TRACK_SAMPLER_TYPE_LAST_FRAME:
            return tk.GetLastFrameValue(keyFrameBaseAddress);
            break;
        }
        return 0;
    }
};

//-----------------------------------------------------------------------------------------//
//  Helper functions to create typed samplers
//-----------------------------------------------------------------------------------------//

TrackSampler CreateDefaultTrackSampler(float time)
{
    TrackSampler rv = {time, TRACK_SAMPLER_TYPE_DEFAULT};
    return rv;
}

/////////////////////////////////////////////////////////////////////////////////

TrackSampler CreateFirstFrameTrackSampler()
{
    TrackSampler rv = {0, TRACK_SAMPLER_TYPE_FIRST_FRAME};
    return rv;
}

/////////////////////////////////////////////////////////////////////////////////

TrackSampler CreateLastFrameTrackSampler()
{
    TrackSampler rv = {0, TRACK_SAMPLER_TYPE_LAST_FRAME};
    return rv;
}

#endif

using System;
using System.Diagnostics;
using Unity.Collections;
using Debug = UnityEngine.Debug;

//////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Toolbox
{
public static class BurstAssert
{
    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    [Conditional("UNITY_DOTS_DEBUG")]
    public static void IsTrue(bool c, in FixedString128Bytes errorMessage)    
    {
        if (c) return;

        Debug.LogError(errorMessage);
        throw new Exception("Assertion Failed");
    }
}
}

using System;
using TMPro;
using Unity.Entities;
using UnityEngine;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
    public class VFXSampleConf: MonoBehaviour
    {
        public TextMeshProUGUI sampleDescription;

        void Start()
        {
        #if !UNITY_6000_0_OR_NEWER
            sampleDescription.text += "\n\n\n<color=red>VFX custom node support was added in version 6000.0. Please make sure you are using Unity 6000.0 or newer for this sample to work properly.</color>";
        #else
        
        #if !RUKHANKA_SAMPLES_WITH_VFX_GRAPH
            sampleDescription.text += "\n\n\n<color=red>Please install the <b>Visual Effect Graph</b> package for the proper functioning of this sample!</color>";
        #endif
            
        #endif
        }
    }
}

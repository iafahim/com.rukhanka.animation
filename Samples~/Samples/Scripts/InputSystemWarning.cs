
using UnityEditor;
using UnityEngine;

namespace Rukhanka.Samples
{
#if !ENABLE_INPUT_SYSTEM
    [InitializeOnLoad]
    class InputSystemWarning
    {
        static InputSystemWarning()
        {
            Debug.LogWarning("Warning: The Rukhanka Samples use the \"Input System\" package for input handling. Input will not work until the \"Input System\" package has been imported.");
        }
    }
#endif
}

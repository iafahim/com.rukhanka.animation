using Rukhanka.Hybrid;
using UnityEditor;
using UnityEngine.UIElements;
    
////////////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Editor
{
[CustomEditor(typeof(GPUAttachmentAuthoring))]
public class GPUAttachmentAuthoringEditor : UnityEditor.Editor
{
    public VisualTreeAsset inspectorXML;
    VisualElement attachedBoneIndexField;
    VisualElement inspector;
    
////////////////////////////////////////////////////////////////////////////////////////

    public override VisualElement CreateInspectorGUI()
    {
        inspector = new VisualElement(); 
        inspectorXML.CloneTree(inspector);
        
        attachedBoneIndexField = inspector.Q("attachedBoneIndex");
        var go = serializedObject.targetObject as GPUAttachmentAuthoring;
        var isChildOfRig = go.GetComponentInParent<RigDefinitionAuthoring>() != null;
        attachedBoneIndexField.style.display = isChildOfRig ? DisplayStyle.None : DisplayStyle.Flex;
        return inspector;
    }
}
}

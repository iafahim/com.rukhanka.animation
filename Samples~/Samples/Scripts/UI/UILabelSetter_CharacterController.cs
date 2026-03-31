using TMPro;
using UnityEditor;
#if UNITY_EDITOR
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#endif
using UnityEngine;
using UnityEngine.UI;

/////////////////////////////////////////////////////////////////////////////////

namespace Rukhanka.Samples
{
class UILabelSetter_CharacterController: MonoBehaviour
{
	public TextMeshProUGUI textElement;
	public Button installCharacterControllerBtn;
	public GameObject netcodeRemoveWarning;
#if UNITY_EDITOR
	static AddRequest Request;
#endif

/////////////////////////////////////////////////////////////////////////////////

    void Awake()
    {
	#if !RUKHANKA_SAMPLES_WITH_CHARACTER_CONTROLLER
		installCharacterControllerBtn.gameObject.SetActive(true);
		textElement.text += "\n\n <color=red>Warning</color>: The Unity Character Controller package is not installed but is required for the proper functioning of this sample. Click the ‘Install Character Controller’ button to install it.";
	#else
		installCharacterControllerBtn.gameObject.SetActive(false);
	#endif
	#if RUKHANKA_SAMPLES_WITH_NETCODE && RUKHANKA_SAMPLES_WITH_PHYSICS
		netcodeRemoveWarning.gameObject.SetActive(true);
	#endif
    }
    
/////////////////////////////////////////////////////////////////////////////////

	public void ImportCharacterControllerPackage()
	{
#if UNITY_EDITOR
		EditorApplication.isPlaying = false;
		Request = Client.Add("com.unity.charactercontroller");
        EditorApplication.update += Progress;
#endif
	}
	
/////////////////////////////////////////////////////////////////////////////////

#if UNITY_EDITOR
	static void Progress()
	{
	   if (Request.IsCompleted)
	   {
		   if (Request.Status == StatusCode.Success)
			   Debug.Log("Installed: " + Request.Result.packageId);
		   else if (Request.Status >= StatusCode.Failure)
			   Debug.Log(Request.Error.message);

		   EditorApplication.update -= Progress;
	   }	
	}
#endif
}
}

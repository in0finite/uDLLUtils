using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;


public class MetaFileUtilsWindow : EditorWindow
{

	Vector2 m_scrollViewPos = Vector2.zero;

//	string	m_oldDllName = "";
//	string	m_newDllName = "";
//	GameObject	m_prefabWithOldDllScripts = null;
//	GameObject	m_prefabWithNewDllScripts = null;
	bool	m_testOnly = true ;

	static	GUILayoutOption	s_expandWidthOption = GUILayout.ExpandWidth (false);

//	System.Reflection.Assembly[] m_foundAssemblies = new System.Reflection.Assembly[0];
	string[] m_assemblyNames = new string[0];
	int m_firstAssemblyIndex = -1;
//	int m_secondAssemblyIndex = -1;

	public string SelectedAssemblyName { get { return m_assemblyNames [m_firstAssemblyIndex]; } }

	string m_chosenSourceXmlFile = "";
	MetaFileUtils.ScriptId[] m_loadedScriptIds = new MetaFileUtils.ScriptId[0];



	[MenuItem("Window/Meta file utils")]
	public static void ShowWindow()
	{
		//Show existing window instance. If one doesn't exist, make one.
		var window = EditorWindow.GetWindow(typeof(MetaFileUtilsWindow));

		window.minSize = new Vector2 (500, 300);
	}


	void OnGUI() {
		
		GUILayout.Space (20);

		m_scrollViewPos = GUILayout.BeginScrollView (m_scrollViewPos);


		if (GUILayout.Button ("Refresh list of dlls", s_expandWidthOption)) {
			m_assemblyNames = MetaFileUtils.GetAllAssembliesWithScripts ().Select (a => a.GetName ().Name).ToArray ();
			m_firstAssemblyIndex = -1;
		//	m_secondAssemblyIndex = -1;
		}

		GUILayout.Label ("Total of " + m_assemblyNames.Length + " dlls");


		GUILayout.Space (20);


	//	GUILayout.Label ("Old dll");
		DrawDllRegion (ref m_firstAssemblyIndex, m_assemblyNames);

	//	GUILayout.Space (20);

	//	GUILayout.Label ("New dll");
	//	DrawDllRegion (ref m_secondAssemblyIndex, m_assemblyNames);


	//	GUILayout.Space (20);

	//	if (GUILayout.Button ("Compare 2 dlls", s_expandWidthOption)) {
	//		Compare2Dlls (m_assemblyNames [m_firstAssemblyIndex], m_assemblyNames [m_secondAssemblyIndex]);
	//	}

		GUILayout.Space (30);


		EditorGUILayout.HelpBox ("Always make a backup before replacing !", MessageType.Warning, true);


		GUILayout.BeginHorizontal ();

		if (GUILayout.Button ("Choose source xml file with script ids", s_expandWidthOption)) {
			string filePath = EditorUtility.OpenFilePanel ("Choose xml file with script ids", "", "xml");
			if (!string.IsNullOrEmpty (filePath)) {
				m_loadedScriptIds = MetaFileUtils.LoadScriptIdsFromXmlFile (filePath);
				m_chosenSourceXmlFile = System.IO.Path.GetFileName (filePath);
				Debug.LogFormat ("Loaded {0} script ids", m_loadedScriptIds.Length);
			}
			EditorGUIUtility.ExitGUI ();
		}

		if (!string.IsNullOrEmpty (m_chosenSourceXmlFile)) {
			GUILayout.Label (m_chosenSourceXmlFile + " - " + m_loadedScriptIds.Length + " script ids");
		}

		GUILayout.EndHorizontal ();


		m_testOnly = GUILayout.Toggle (m_testOnly, new GUIContent ("Test only", "If checked, assets will not be modified, but you will get output log"));

		if (GUILayout.Button ("Replace", s_expandWidthOption)) {
			this.Replace ();
		}


		GUILayout.EndScrollView ();

		GUILayout.Space (15);

	}

	static	void	DrawDllRegion( ref int selectedDllIndex, string[] assemblyNames ) {

	//	dllName = EditorGUILayout.TextField (dllName, s_expandWidthOption);

	//	prefabWithDllScripts = (GameObject) EditorGUILayout.ObjectField ("prefab with dll scripts", prefabWithDllScripts, typeof(GameObject),
	//		false, s_expandWidthOption);

		selectedDllIndex = GUILayout.SelectionGrid (selectedDllIndex, assemblyNames, 4);

		GUILayout.Space (5);

		if (GUILayout.Button ("List all scripts from dll", s_expandWidthOption)) {
			
			int count = 0;
			foreach (var scriptId in MetaFileUtils.ListDllScripts(assemblyNames[selectedDllIndex])) {
				Debug.Log (scriptId.type + ", guid: " + scriptId.guid + ", fileId: " + scriptId.fileId );
				count++;
			}
			Debug.Log ("Total of " + count + " scripts");

		}

		if (GUILayout.Button ("Save script ids to xml file", s_expandWidthOption)) {

			var scriptIds = MetaFileUtils.ListDllScripts (assemblyNames [selectedDllIndex]).ToArray ();

			string filePath = EditorUtility.SaveFilePanel( "Save script ids to file", "", "scriptIds.xml", "xml");

			if (!string.IsNullOrEmpty (filePath)) {
				MetaFileUtils.SaveScriptIdsToXmlFile (scriptIds, filePath);
				Debug.LogFormat ("Saved {0} script ids to file", scriptIds.Length);
			}

			EditorGUIUtility.ExitGUI ();
		}

		/*
		if (GUILayout.Button ("List all scripts using prefab", s_expandWidthOption)) {

			var scriptIds = MetaFileUtils.FindPrefabScriptIds (prefabWithDllScripts);
			foreach (var id in scriptIds) {
				Debug.Log ("fileId " + id.fileId + ", guid " + id.guid);
			}
			Debug.Log ("Total " + scriptIds.Count + " scripts");

		}

		if (GUILayout.Button ("Compare script ids", s_expandWidthOption)) {

			var scriptIdsFromPrefab = MetaFileUtils.FindPrefabScriptIds (prefabWithDllScripts);
			var scriptIdsFromDll = MetaFileUtils.ListDllScripts (dllName).ToList();

			int countNotFound = 0;

			foreach (var id in scriptIdsFromPrefab) {
				if(!scriptIdsFromDll.Exists( id_ => id_.fileId == id.fileId && id_.guid == id.guid)) {
					Debug.LogWarning("script with fileId " + id.fileId + " not found in dll scripts" );
					countNotFound++;
				}
			}

			Debug.Log ("Comparison finished - num scripts from prefab " + scriptIdsFromPrefab.Count +
				", num scripts from dll " + scriptIdsFromDll.Count + ", num not matching " + countNotFound);
		}
		*/

	}

	/// <summary>
	/// Compare scripts from 2 dlls by type.
	/// </summary>
	static void Compare2Dlls(string dllName1, string dllName2)
	{
		var scripts1 = MetaFileUtils.ListDllScripts (dllName1).ToList ();
		var scripts2 = MetaFileUtils.ListDllScripts (dllName2).ToList ();

		int numSameTypes = 0;

		foreach (var script in scripts1) {

			// find script in other dll which has the same type name
			var otherScript = scripts2.Find (s => s.type.FullName == script.type.FullName);

			if (otherScript != null) {
				numSameTypes++;
			//	Debug.LogFormat ("Found scripts with same type:");
			}
		}

		Debug.LogFormat ("Num scripts in first dll {0}, num scripts in second dll {1}, num scripts with same type {2}",
			scripts1.Count, scripts2.Count, numSameTypes);
		
	}

	void Replace ()
	{
		if (string.IsNullOrEmpty (m_chosenSourceXmlFile))
			throw new System.Exception ("Source xml file is not selected");

	//	var newScriptIds = MetaFileUtils.ListDllScripts (m_assemblyNames [m_secondAssemblyIndex]);

	//	MetaFileUtils.ReplaceScriptsInProject (m_loadedScriptIds.ToList(), newScriptIds.ToList(), 
	//		m_testOnly);
	
		MetaFileUtils.ReplaceScriptsFromDllWithThoseInProject (m_loadedScriptIds.ToList (), m_testOnly);

	}

}


using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.IO;
using System.Xml.Linq;


public static class MetaFileUtils
{

	public class ScriptId
	{
		public ScriptId () { }

		public ScriptId (string fileId, string guid, Type type)
		{
			this.fileId = fileId;
			this.guid = guid;
			this.type = type;
		}

		public string fileId;
		public string guid;
		public Type	type;
		private string m_typeName = "";
		public string TypeName { 
			get { if (this.type != null) return this.type.FullName; else return m_typeName; }
			set { m_typeName = value; }
		}

	}


	public const string kAssemblyWithScripts = "Assembly-CSharp";



	public static HashSet<System.Reflection.Assembly> GetAllAssembliesWithScripts ()
	{

		var assemblies = new HashSet<System.Reflection.Assembly> ();
		var scripts = Resources.FindObjectsOfTypeAll<MonoScript> ();

		foreach (var class_ in scripts.Select(script => script.GetClass()).Where( c => c != null)) {
			assemblies.Add (class_.Assembly);
		}

		return assemblies;
	}

	public static void ListAllAssembliesWithScripts ()
	{
		
		int count = 0;
		foreach (string name in GetAllAssembliesWithScripts().Select(a => a.GetName().Name)) {
			Debug.Log (name);
			count++;
		}

		Debug.Log ("Total " + count + " assemblies");

	}

	public	static List<ScriptId> FindPrefabScriptIds( GameObject prefab ) {

		//	var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
		string prefabAssetPath = AssetDatabase.GetAssetPath (prefab);

		return FindPrefabScriptIds( prefabAssetPath );
	}

	/// Returns ids of all scripts attached to prefab.
	public	static List<ScriptId> FindPrefabScriptIds( string prefabAssetPath ) {

		var scriptIds = new List<ScriptId> ();

		var lines = File.ReadAllLines (prefabAssetPath);


		int indexBegin = System.Array.FindIndex (lines, l => l.Contains("  m_Component:") );

		if (indexBegin < 0)
			throw new System.Exception ("Failed to read prefab script ids");

		int indexEnd = System.Array.FindIndex (lines, indexBegin + 1, l => !l.StartsWith ("  - component: "));
		if(indexEnd - indexBegin < 1)
			throw new System.Exception ("Failed to read prefab script ids");

		var scriptsInternalIds = new List<string> ();
		for (int i = indexBegin + 1; i < indexEnd; i++) {

			int ind = lines [i].IndexOf ("fileID: ");
			int endInd = lines [i].LastIndexOf ("}");
			string fileId = lines[i].Substring( ind + 9, endInd - ind - 9);

			scriptsInternalIds.Add (fileId);
		}


		int numReplaced = 0;

		foreach (var internalId in scriptsInternalIds) {

			int lastInd = System.Array.FindLastIndex (lines, l => l.Contains(internalId) );

			if (! lines [lastInd + 1].StartsWith ("MonoBehaviour:")) {
				// the component is not MonoBehaviour
				continue;
			}

			string search = "  m_Script: {fileID: ";

			int lineInd = System.Array.FindIndex( lines, lastInd + 1, l => l.StartsWith(search) );
			string line = lines [lineInd];

			int commaInd = line.IndexOf (',');
			string fileId = line.Substring( search.Length, commaInd - search.Length);

			search = "guid: ";
			int ind = line.IndexOf (search);
			commaInd = line.IndexOf (',', ind);

			string guid = line.Substring (ind + search.Length, commaInd - ind - search.Length);

//			if (idsToReplace != null) {
//				
//				ScriptId scriptId;
//				if (idsToReplace.TryGetValue (guid, out scriptId)) {
//					// replace ids
//
//					numReplaced++;
//				}
//
//			//	lines[lineInd] = lines[lineInd].Replace( fileId, );
//			//	lines[lineInd] = lines[lineInd].Replace( guid, );
//			}

			scriptIds.Add ( new ScriptId(fileId, guid, null) );
		}

	//	Debug.Log ("Replaced " + numReplaced + " ids inside " + prefabAssetPath);

		return scriptIds;
	}

	public	static	IEnumerable<ScriptId>	ListDllScripts( string dllName ) {

		// returns guids
		//	var guids = AssetDatabase.FindAssets ("t:MonoScript");

		var scripts = Resources.FindObjectsOfTypeAll<MonoScript> ();
		foreach (var script in scripts) {
			if (null == script)
				continue;
			var Class = script.GetClass ();
			if (null == Class)
				continue;
			if (Class.Assembly.GetName ().Name == dllName) {
				var guid = AssetDatabase.AssetPathToGUID (AssetDatabase.GetAssetPath (script));
				var fileId = FileIDUtil.Compute (Class);

				yield return new ScriptId( fileId.ToString(), guid, Class );
			}
		}

	}

	public static void SaveScriptIdsToXmlFile (ScriptId[] scriptIds, string filePath)
	{

		var xmlEl = new XElement("scriptIds", scriptIds.Select( x => new XElement("scriptId", 
			new XAttribute("guid", x.guid), 
			new XAttribute("fileId", x.fileId), 
			new XAttribute("typeName", x.TypeName))));

	//	xmlEl.WriteTo (System.Xml.XmlWriter.Create (filePath, new System.Xml.XmlWriterSettings () { Indent = true }));
		xmlEl.Save (filePath);

	}

	public static ScriptId[] LoadScriptIdsFromXmlFile (string filePath)
	{
		var xmlEl = XElement.Load (filePath);

		return xmlEl.Elements ().Select (el => new ScriptId () {
			guid = el.Attribute ("guid").Value,
			fileId = el.Attribute ("fileId").Value,
			TypeName = el.Attribute ("typeName").Value,
		})
			.ToArray ();
		
	}

	public	static	void	ReplaceScriptsFrom2Dlls( GameObject prefabWithOldDllScripts,
		GameObject prefabWithNewDllScripts, bool testOnly ) {

		var oldIds = FindPrefabScriptIds (prefabWithOldDllScripts).Select (
			             id => new ScriptId (id.fileId, id.guid, null)).ToList ();
		var newIds = FindPrefabScriptIds (prefabWithNewDllScripts).Select (
			             id => new ScriptId (id.fileId, id.guid, null)).ToList ();

		ReplaceScriptsInProject (oldIds, newIds, testOnly);

	}

	public static void ReplaceScriptsFromDllWithThoseInProject (List<ScriptId> dllScripts, bool testOnly)
	{
		var scriptsInProject = ListDllScripts (kAssemblyWithScripts).ToList ();

		// we have to change file id for all scripts in project
		// Unity uses some 'magic' number for them

		foreach (var id in scriptsInProject) {
			id.fileId = "11500000";
		}

		// replace ids
		ReplaceScriptsInProject( dllScripts, scriptsInProject, testOnly );

	}

	/// <summary>
	/// Strings represent assembly names.
	/// testOnly - if true, asset will not be modified, but you will get output log.
	/// </summary>
	public	static	void	ReplaceScriptsFrom2Dlls( string oldDll, string newDll, bool testOnly ) {

		// find scripts inside dlls

		var oldIds = ListDllScripts (oldDll).ToList ();
		var newIds = ListDllScripts (newDll).ToList ();

		if (0 == oldIds.Count) {
			Debug.LogError ("No scripts found in " + oldDll);
			return;
		}

		if (0 == newIds.Count) {
			Debug.LogError ("No scripts found in " + newDll);
			return;
		}

		ReplaceScriptsInProject (oldIds, newIds, testOnly);

	}

	public	static	void	ReplaceScriptsInProject( List<ScriptId> oldIds, List<ScriptId> newIds, bool testOnly ) {
		
		// replace ids for every prefab and scene in a project

		var prefabPaths = new List<string> ();
		var scenePaths = new List<string> ();

		foreach (var assetPath in System.IO.Directory.GetFiles( Application.dataPath, "*",
			System.IO.SearchOption.AllDirectories )) {

			if (assetPath.EndsWith (".prefab"))
				prefabPaths.Add (assetPath);
			else if (assetPath.EndsWith (".unity"))
				scenePaths.Add (assetPath);
		}

		Debug.Log ("Found " + prefabPaths.Count + " prefabs to replace");
		Debug.Log ("Found " + scenePaths.Count + " scenes to replace");

		int numLinesReplaced = 0;
		foreach (var assetPath in prefabPaths.Concat (scenePaths)) {
			Debug.Log ("Processing " + assetPath);

			numLinesReplaced += ReplaceScriptIds( assetPath, oldIds, newIds, testOnly );
		}

		Debug.LogFormat ("Replacing finished - {0} lines replaced", numLinesReplaced);

	}

	/// <summary>
	/// testOnly - if true, asset will not be modified, but you will get output log.
	/// </summary>
	public	static int ReplaceScriptIds( string assetPath, List<ScriptId> oldIds, List<ScriptId> newIds, bool testOnly ) {

		var lines = File.ReadAllLines (assetPath);

		int countReplaced = 0;
		var replacedScriptNames = new HashSet<string> ();

		for (int i = 0; i < lines.Length; i++) {

			string oldLine = lines [i];
			lines [i] = ReplaceScriptIdsInLine (lines [i], oldIds, newIds, replacedScriptNames);
			if (oldLine != lines [i]) {
				countReplaced++;
			}

		}

		if (countReplaced > 0) {
			if (!testOnly) {
				File.WriteAllLines (assetPath, lines);
			}
		}

		var sb = new System.Text.StringBuilder ();
		sb.Append ("Replaced " + countReplaced + " lines in " + assetPath + "\n");
		foreach (var name in replacedScriptNames)
			sb.Append (name + "\n");
		Debug.Log (sb.ToString());

		return countReplaced;
	}

	public static string ReplaceScriptIdsInLine( string line, List<ScriptId> oldIds, List<ScriptId> newIds,
		HashSet<string> replacedScriptNames ) {

	//	if (oldIds.Count != newIds.Count)
	//		throw new ArgumentException ("Number of old and new ids must be the same");

		// find guid and fileId inside a line

		string guid = "";
		string fileId = "";
		if (!GetGuidAndFileIdFromLine (line, ref guid, ref fileId))
			return line;
		
		// check if this guid and fileId match any of old scripts
		ScriptId matchingOldScriptId = oldIds.Find( id => id.fileId == fileId && id.guid == guid );
		if (matchingOldScriptId != null) {
			// found matching script

			// replace it with new script
			// replace both file id and guid

			// find new script
		//	var matchingNewScriptIds = new List<ScriptId>() { newIds[ oldIds.IndexOf(matchingOldScriptId) ] } ;
			var matchingNewScriptIds = newIds.FindAll (id => id.TypeName == matchingOldScriptId.TypeName);

			if (matchingNewScriptIds.Count > 1) {
				Debug.LogError ("Found " + matchingNewScriptIds.Count + " matching scripts (new) for " + matchingOldScriptId.TypeName);
			} else if (0 == matchingNewScriptIds.Count) {
				// no matches
				Debug.LogWarning ("No match for " + matchingOldScriptId.TypeName);
			} else {
				// found exactly 1 new script which matches
				// replace ids

				var newScriptId = matchingNewScriptIds [0];

				line = line.Replace ("guid: " + guid, "guid: " + newScriptId.guid);

				line = line.Replace ("fileID: " + fileId, "fileID: " + newScriptId.fileId);

			//	replacedScriptNames.Add (newScriptId.type.ToString ());
			}

		} else {
		//	string path = AssetDatabase.GUIDToAssetPath (guid);
		//	Debug.LogWarning ("no match for old script - guid " + guid + ", fileID " + fileId + ", path " + path);

			if (oldIds.Exists (id => id.guid == guid)) {
				Debug.LogError ("Found a script which matches guid " + guid + ", but has unknown fileID " + fileId);
			}

		}

		return line;
	}

	public	static	bool	GetGuidAndFileIdFromLine( string line, ref string guid, ref string fileId ) {

		string search = "guid: ";

		int index = line.IndexOf (search);
		if (index < 0)
			return false;

		int indexEnd = System.Array.FindIndex (line.ToCharArray (), index + search.Length, c => !char.IsLetterOrDigit (c));

		guid = line.Substring (index + search.Length, indexEnd - index - search.Length);


		search = "fileID: ";
		index = line.IndexOf (search);
		if (index < 0)
			return false;

		indexEnd = System.Array.FindIndex (line.ToCharArray (), index + search.Length, c => !char.IsLetterOrDigit (c) && c != '-');
		fileId = line.Substring (index + search.Length, indexEnd - index - search.Length);

		if ("" == fileId) {
			int jkdsfhd = 1353;
		}


		return true;
	}

}





// Taken from http://www.superstarcoders.com/blogs/posts/md4-hash-algorithm-in-c-sharp.aspx
// Probably not the best implementation of MD4, but it works.
public class MD4 : HashAlgorithm
{
	private uint _a;
	private uint _b;
	private uint _c;
	private uint _d;
	private uint[] _x;
	private int _bytesProcessed;

	public MD4()
	{
		_x = new uint[16];

		Initialize();
	}

	public override void Initialize()
	{
		_a = 0x67452301;
		_b = 0xefcdab89;
		_c = 0x98badcfe;
		_d = 0x10325476;

		_bytesProcessed = 0;
	}

	protected override void HashCore(byte[] array, int offset, int length)
	{
		ProcessMessage(Bytes(array, offset, length));
	}

	protected override byte[] HashFinal()
	{
		try
		{
			ProcessMessage(Padding());

			return new [] {_a, _b, _c, _d}.SelectMany(word => Bytes(word)).ToArray();
		}
		finally
		{
			Initialize();
		}
	}

	private void ProcessMessage(IEnumerable<byte> bytes)
	{
		foreach (byte b in bytes)
		{
			int c = _bytesProcessed & 63;
			int i = c >> 2;
			int s = (c & 3) << 3;

			_x[i] = (_x[i] & ~((uint)255 << s)) | ((uint)b << s);

			if (c == 63)
			{
				Process16WordBlock();
			}

			_bytesProcessed++;
		}
	}

	private static IEnumerable<byte> Bytes(byte[] bytes, int offset, int length)
	{
		for (int i = offset; i < length; i++)
		{
			yield return bytes[i];
		}
	}

	private IEnumerable<byte> Bytes(uint word)
	{
		yield return (byte)(word & 255);
		yield return (byte)((word >> 8) & 255);
		yield return (byte)((word >> 16) & 255);
		yield return (byte)((word >> 24) & 255);
	}

	private IEnumerable<byte> Repeat(byte value, int count)
	{
		for (int i = 0; i < count; i++)
		{
			yield return value;
		}
	}

	private IEnumerable<byte> Padding()
	{
		return Repeat(128, 1)
			.Concat(Repeat(0, ((_bytesProcessed + 8) & 0x7fffffc0) + 55 - _bytesProcessed))
			.Concat(Bytes((uint)_bytesProcessed << 3))
			.Concat(Repeat(0, 4));
	}

	private void Process16WordBlock()
	{
		uint aa = _a;
		uint bb = _b;
		uint cc = _c;
		uint dd = _d;

		foreach (int k in new [] { 0, 4, 8, 12 })
		{
			aa = Round1Operation(aa, bb, cc, dd, _x[k], 3);
			dd = Round1Operation(dd, aa, bb, cc, _x[k + 1], 7);
			cc = Round1Operation(cc, dd, aa, bb, _x[k + 2], 11);
			bb = Round1Operation(bb, cc, dd, aa, _x[k + 3], 19);
		}

		foreach (int k in new [] { 0, 1, 2, 3 })
		{
			aa = Round2Operation(aa, bb, cc, dd, _x[k], 3);
			dd = Round2Operation(dd, aa, bb, cc, _x[k + 4], 5);
			cc = Round2Operation(cc, dd, aa, bb, _x[k + 8], 9);
			bb = Round2Operation(bb, cc, dd, aa, _x[k + 12], 13);
		}

		foreach (int k in new [] { 0, 2, 1, 3 })
		{
			aa = Round3Operation(aa, bb, cc, dd, _x[k], 3);
			dd = Round3Operation(dd, aa, bb, cc, _x[k + 8], 9);
			cc = Round3Operation(cc, dd, aa, bb, _x[k + 4], 11);
			bb = Round3Operation(bb, cc, dd, aa, _x[k + 12], 15);
		}

		unchecked
		{
			_a += aa;
			_b += bb;
			_c += cc;
			_d += dd;
		}
	}

	private static uint ROL(uint value, int numberOfBits)
	{
		return (value << numberOfBits) | (value >> (32 - numberOfBits));
	}

	private static uint Round1Operation(uint a, uint b, uint c, uint d, uint xk, int s)
	{
		unchecked
		{
			return ROL(a + ((b & c) | (~b & d)) + xk, s);
		}
	}

	private static uint Round2Operation(uint a, uint b, uint c, uint d, uint xk, int s)
	{
		unchecked
		{
			return ROL(a + ((b & c) | (b & d) | (c & d)) + xk + 0x5a827999, s);
		}
	}

	private static uint Round3Operation(uint a, uint b, uint c, uint d, uint xk, int s)
	{
		unchecked
		{
			return ROL(a + (b ^ c ^ d) + xk + 0x6ed9eba1, s);
		}
	}
}

public static class FileIDUtil
{
	public static int Compute(Type t)
	{
		string toBeHashed = "s\0\0\0" + t.Namespace + t.Name;

		using (HashAlgorithm hash = new MD4())
		{
			byte[] hashed = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(toBeHashed));

			int result = 0;

			for(int i = 3; i >= 0; --i)
			{
				result <<= 8;
				result |= hashed[i];
			}

			return result;
		}
	}
}


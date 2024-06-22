using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Unity.Plastic.Newtonsoft.Json;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// UnitySubProjectManagement (USPM)
/// </summary>
namespace TANAKADOREI.UnityEditor.USPM
{
	public class Debug
	{
		/// <summary>
		/// 강제 로그 금지
		/// </summary>
		public static bool NoLogging = false;
		public static bool Enable = true;

		public static void Log(object o) { if (!NoLogging && Enable) UnityEngine.Debug.Log(o); }

		public static void LogWarning(object o) { if (!NoLogging && Enable) UnityEngine.Debug.LogWarning(o); }

		public static void LogError(object o) { UnityEngine.Debug.LogError(o); }
	}

	public static class XML_Utilities
	{
		/// <summary>
		/// 어트리 뷰트 무시되므로 주의
		/// </summary>
		/// <param name="doc"></param>
		/// <param name="path"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public static XmlNode CreateNodeWithPath(XmlDocument doc, string path, string value = null)
		{
			string[] parts = path.Trim('/').Split('/');
			XmlNode cur_node = doc.DocumentElement;

			foreach (string part in parts)
			{
				XmlNode node = null;
				foreach (XmlNode sub_node in cur_node)
				{
					if (sub_node.Name == part)
					{
						node = sub_node;
						break;
					}
				}

				if (node == null)
				{
					XmlElement e = doc.CreateElement(part);
					cur_node.AppendChild(e);
					cur_node = e;
				}
				else
				{
					cur_node = node;
				}
			}

			if (value != null) cur_node.InnerText = value;

			return cur_node;
		}
	}

	public class XML_Tools
	{
		static XmlDocument g_doc = null;
		static string g_file_path = null;

		public static void Open(string file_path)
		{
			if (g_doc != null) throw new Exception("already opened");
			g_doc = new();
			g_file_path = file_path;
			g_doc.Load(g_file_path);
		}

		public static void Close()
		{
			if (g_doc == null) throw new Exception("already closed");
			g_doc.Save(g_file_path);
		}

		/// <param name="node_path">`a/b/c`</param>
		public static void SetData(string node_path, string data, bool create_if = true)
		{
			try
			{
				g_doc.SelectSingleNode(node_path).InnerText = data?.Length <= 0 ? "" : data;
			}
			catch
			{
				if (create_if) XML_Utilities.CreateNodeWithPath(g_doc, node_path, data);
			}
		}

		/// <param name="node_path">`a/b/c`</param>
		public static string GetData(string node_path)
		{
			try
			{
				var data = g_doc.SelectSingleNode(node_path).InnerText;
				return data?.Length <= 0 ? null : data;
			}
			catch
			{
				return null;
			}
		}
	}

	public class XML_Processor
	{
		public abstract class NodeProcessor
		{
			/// <summary>
			/// 동일한 경로의 노드를 찾는다.
			/// A/B/C이면 C를 찾음
			/// </summary>
			public readonly string TargetNodePath;

			public NodeProcessor(string base_path, string name)
			{
				TargetNodePath = $"/{base_path}/{name}";
			}

			public virtual void OnPrepare(XmlDocument source_doc) { }

			/// <summary>
			/// Name과 동일한 이름을 찾는다
			/// </summary>
			/// <param name="source_node"></param>
			public virtual void OnFoundIt(XmlDocument source_doc, XmlNode source_node) { }
		}

		private XmlDocument SourceDoc;
		private readonly string SourceDocPath;
		private readonly NodeProcessor[] Processors;

		public XML_Processor(string source_doc_path, params NodeProcessor[] processor)
		{
			SourceDocPath = source_doc_path;
			Processors = processor;
		}

		public void Processing()
		{
			SourceDoc = new();
			SourceDoc.Load(SourceDocPath);

			foreach (var proc in Processors)
			{
				proc.OnPrepare(SourceDoc);
				foreach (XmlNode node in SourceDoc.SelectNodes(proc.TargetNodePath))
				{
					proc.OnFoundIt(SourceDoc, node);
				}
			}


			SourceDoc.Save(SourceDocPath);
		}
	}

	public abstract class UnityCSharpProjXmlNodeProcessor : XML_Processor.NodeProcessor
	{
		const string CSPROJ_XML__ROOT_NODE = "Project";

		public UnityCSharpProjXmlNodeProcessor(string base_path, string name) : base(base_path, name)
		{
		}

		/// <summary>
		/// 참조 추가 하기
		/// </summary>
		public class SetReferences : UnityCSharpProjXmlNodeProcessor
		{
			const string NODE__ITEM_GROUP = "ItemGroup";
			private readonly USPManifest.SubProjectInfo TargetProject;
			private readonly HashSet<USPManifest.ReferencePreset> RefList;
			private XmlNode m_target_item_group = null;

			/// <summary>
			/// 
			/// </summary>
			/// <param name="opt_set">최적 세팅으로</param>
			/// <param name="ref_list">추가할 리스트[t:abs f:rel][include][path]</param>
			public SetReferences(IEnumerable<USPManifest.ReferencePreset> ref_list, USPManifest.SubProjectInfo target_project) : base(CSPROJ_XML__ROOT_NODE, NODE__ITEM_GROUP)
			{
				RefList = new(ref_list);
				TargetProject = target_project;
			}

			public override void OnPrepare(XmlDocument source_doc)
			{
				// 어트리 뷰트가 없는 ItemGroup을 찾는다
				{
					foreach (XmlNode node in source_doc.SelectNodes(TargetNodePath))
					{
						if (node.Attributes?.Count == 0)
						{
							m_target_item_group = node;
							break;
						}
					}

					if (m_target_item_group == null)
					{
						m_target_item_group = source_doc.CreateElement(NODE__ITEM_GROUP);
						source_doc.SelectSingleNode(CSPROJ_XML__ROOT_NODE).AppendChild(m_target_item_group);
					}
				}

				if (m_target_item_group == null)
				{
					Debug.LogError($"Failed to process {TargetNodePath} path");
					return;
				}

				// 참조 리스트의 경로값을 최종 원하는 경로로 변환한다
				foreach (var path in RefList)
				{
					bool req_abs_to_rel = false;
					path.DLL_Path = Path.GetFullPath(path.DLL_Path);

					if (path.OptimalSetting)
					{
						// 유니티 프로젝트 디렉터리가 포함된 경로라면 상대경로로 변환
						req_abs_to_rel = path.DLL_Path.StartsWith(USPM_ConstDataList.UnityProjectDirPath);
					}
					else
					{
						req_abs_to_rel = path.ToRelativePath;
					}

					if (req_abs_to_rel)
					{
						path.DLL_Path = AbsToRelPath(TargetProject.CurrentProjFilePath, path.DLL_Path);
					}
					else
					{
						try
						{
							path.DLL_Path = Path.GetFullPath(path.DLL_Path);
						}
						catch (Exception e)
						{
							Debug.LogError($"unknown path {path.DLL_Path}, {e}");
							path.DLL_Path = null;
						}
					}
				}

				const string NODE__REFERENCE = "Reference";
				const string ATTR__INCLUDE = "Include";
				const string SUB_NODE__HINT_PATH = "HintPath";

				// 이제부터 프리셋의 DLL_Path는 null일 가능성이 있다.
				{
					// 존재하는 노드 부터 덮어쓰기
					foreach (XmlNode ref_node in m_target_item_group)
					{
						string attr_include_text = null;
						try
						{
							if (ref_node.Name != NODE__REFERENCE) continue;
							attr_include_text = ref_node.Attributes[ATTR__INCLUDE].InnerText;
							if (attr_include_text?.Length <= 0) continue;
							if (ref_node[SUB_NODE__HINT_PATH].InnerText.Length <= 0) continue;
						}
						catch
						{
							continue;
						}

						USPManifest.ReferencePreset preset = new(attr_include_text);

						if (RefList.TryGetValue(preset, out preset))
						{
							if (preset.DLL_Path == null) continue;
							ref_node[SUB_NODE__HINT_PATH].InnerText = preset.DLL_Path;
							RefList.Remove(preset);
						}
					}

					// 노드 추가
					foreach (var preset in RefList)
					{
						if (preset.DLL_Path == null) continue;

						var ref_node = source_doc.CreateElement(NODE__REFERENCE);
						var sub_hint_node = source_doc.CreateElement(SUB_NODE__HINT_PATH);
						var ref_node_attr = source_doc.CreateAttribute(ATTR__INCLUDE);

						ref_node_attr.InnerText = preset.Include;
						sub_hint_node.InnerText = preset.DLL_Path;

						ref_node.Attributes.Append(ref_node_attr);
						ref_node.AppendChild(sub_hint_node);

						m_target_item_group.AppendChild(ref_node);
					}
				}
			}

			public static string AbsToRelPath(string base_file_path, string target_file_path)
			{
				try
				{
					Uri baseUri = new Uri(base_file_path);
					Uri targetUri = new Uri(target_file_path);
					Uri relativeUri = baseUri.MakeRelativeUri(targetUri);
					return Uri.UnescapeDataString(relativeUri.ToString());
				}
				catch (Exception e)
				{
					Debug.LogError($"The path could not be converted to a relative path. : {target_file_path}, {e}");
					return null;
				}
			}
		}

		/// <summary>
		/// 유니티 csproj에서 참조 리스트 뽑기
		/// </summary>
		public class ParseReferencesInUnityProject : UnityCSharpProjXmlNodeProcessor
		{
			/// <summary>
			/// 무시할 접두사
			/// </summary>
			public readonly static string[] IGNORE_FILTER = new string[]
			{
				"netstandard",
				"Microsoft",
				"System",
				"mscorlib"
			};

			private readonly USPManifest m_manifest;
			private readonly string BaseDirDirectory;

			public ParseReferencesInUnityProject(string unity_csproj_path, USPManifest manifest) : base(CSPROJ_XML__ROOT_NODE, "ItemGroup")
			{
				m_manifest = manifest;
				BaseDirDirectory = Path.GetFullPath(Path.GetDirectoryName(unity_csproj_path));
			}

			private void AddDLL(string include, string path)
			{
				string result_path;
				bool original_path_is_absolute;

				try
				{
					if (original_path_is_absolute = Path.IsPathRooted(path))
					{
						result_path = Path.GetFullPath(path);
					}
					else
					{
						result_path = Path.GetFullPath(Path.Combine(BaseDirDirectory, path));
					}
				}
				catch (Exception e)
				{
					result_path = null;
					Debug.LogWarning($"처리하지 못한 DLL : {include}->{path}, {e}");
					return;
				}

				if (result_path != null)
				{
					var ref_info = new USPManifest.ReferenceInfo(include);

					if (m_manifest.UnityProjectHasReferences.TryGetValue(ref_info, out ref_info))
					{
						ref_info.AbsoluteDLL_HintPathList.Add(result_path);
					}
					else
					{
						m_manifest.UnityProjectHasReferences.Add(new(include, result_path));
					}
				}
			}

			public override void OnFoundIt(XmlDocument source_doc, XmlNode source_node)
			{
				foreach (XmlNode node in source_node)
				{
					if (node.Name == "Reference") // include네임스페이스별로 분리
					{
						string lib_name = node.Attributes["Include"].InnerText;
						string lib_path = node["HintPath"].InnerText;

						if (IGNORE_FILTER.Any(i => lib_name.StartsWith(i))) continue;

						AddDLL(lib_name, lib_path);
					}
				}
			}
		}
	}

	public static class USPM_ConstDataList
	{
		public const string USPM = "USPM";

		private static string CreateIfNotFoundDir(string path)
		{
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}
			return path;
		}

		/// <summary>
		/// 유니티 프로젝트의 부모 디렉터리 경로
		/// `ParentDir/`
		/// </summary>
		/// <returns></returns>
		public static string UnityProjectParentDirPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
		/// <summary>
		/// 현재 유니티 프로젝트 경로
		/// `ParentDir/MyUnityProject`
		/// </summary>
		/// <returns></returns>
		public static string UnityProjectDirPath => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
		/// <summary>
		/// 하위 프로젝트가 있는 디렉터리
		/// `ParentDir/MyUnityProject/USPM`
		/// </summary>
		public static string USPM_DirPath => CreateIfNotFoundDir(Path.GetFullPath(Path.Combine(UnityProjectDirPath, USPM)));
		/// <summary>
		/// USPM 매니페스트 파일 경로
		/// `ParentDir/MyUnityProject/USPM/MANIFEST.json`
		/// </summary>
		/// <returns></returns>
		public static string USPM_ManifestFilePath => Path.GetFullPath(Path.Combine(USPM_DirPath, "MANIFEST.json"));
		/// <summary>
		/// USPM 기본 출력 위치
		/// </summary>
		public static string USPM_BuildOutputDirPath => CreateIfNotFoundDir(Path.GetFullPath(Path.Combine(USPM_DirPath, "BIN_OUT")));
		/// <summary>
		/// 현재 유니티 프로젝트 이름
		/// `MyUnityProject`
		/// </summary>
		/// <returns></returns>
		public static string UnityProjectName => Path.GetFullPath(Path.GetFileName(UnityProjectDirPath));
		/// <summary>
		/// Assembly-CSharp.csproj 파일 경로
		/// </summary>
		/// <returns></returns>
		public static string UnityProject_ProjFilePath => Path.GetFullPath(Path.Combine(UnityProjectDirPath, "Assembly-CSharp.csproj"));
		/// <summary>
		/// Assembly-CSharp-Editor.csproj 파일 경로
		/// </summary>
		/// <returns></returns>
		public static string UnityProject_EditorProjFilePath => Path.GetFullPath(Path.Combine(UnityProjectDirPath, "Assembly-CSharp-Editor.csproj"));

		public static string UnityProjectPluginsDirPath => CreateIfNotFoundDir(Path.GetFullPath(Path.Combine(UnityProjectAssetsDirPath, "Plugins")));
		public static string UnityProjectAssetsDirPath => CreateIfNotFoundDir(Path.GetFullPath(Path.Combine(UnityProjectDirPath, "Assets")));

		public static string GetBaseOutputDirPath(string project_name)
		{
			return CreateIfNotFoundDir(Path.GetFullPath(Path.Combine(USPM_BuildOutputDirPath, project_name)));
		}
	}

	/// <summary>
	/// UnityProject/MyProject/
	/// UnityProject/MyProject/Assets
	/// UnityProject/MyProject/USPM/
	/// UnityProject/MyProject/USPM/MANIFEST.json
	/// UnityProject/MyProject/USPM/<SubProjects...>
	/// UnityProject/MyProject/USPM/<SubProjects...>/*.csproj
	/// </summary>
	[Serializable]
	public class USPManifest
	{
		[Serializable]
		public class ReferencePreset
		{
			[SerializeField]
			public string Include = "";
			[SerializeField]
			public bool OptimalSetting = true;
			[SerializeField]
			public bool ToRelativePath = false;
			[SerializeField]
			public string DLL_Path = "";

			public ReferencePreset() { }

			public ReferencePreset(string include)
			{
				Include = include;
			}

			public ReferencePreset(string include, bool optimal_setting, bool to_rel_path, string dll_path)
			{
				Include = include;
				OptimalSetting = optimal_setting;
				ToRelativePath = to_rel_path;
				DLL_Path = dll_path;
			}

			public override bool Equals(object obj)
			{
				return obj is ReferencePreset preset && preset.Include == Include;
			}

			public override int GetHashCode()
			{
				return Include.GetHashCode();
			}
		}

		public class ReferenceInfo
		{
			public string Include = null;
			public HashSet<string> AbsoluteDLL_HintPathList = new();

			public ReferenceInfo(string include, string path = null)
			{
				Include = include;
				AbsoluteDLL_HintPathList ??= new();
				if (path != null) AbsoluteDLL_HintPathList.Add(path);
			}

			public override bool Equals(object obj)
			{
				return obj is ReferenceInfo info && info.Include == Include;
			}

			public override int GetHashCode()
			{
				return Include.GetHashCode();
			}
		}

		//[CreateAssetMenu(fileName = "SubProjectInfo.asset", menuName = "SubProjectInfoAsset")]
		private class SubProjectRenderingClass : ScriptableObject
		{
			public const string ASSET_PATH_USPM_DATA = nameof(USPM_DATA);
			[SerializeField]
			public SubProjectInfo USPM_DATA = null;

			public static SubProjectRenderingClass New(SubProjectInfo data)
			{
				var asset = CreateInstance<SubProjectRenderingClass>();
				asset.USPM_DATA = data;
				return asset;
			}

			public static void Delete(SubProjectRenderingClass asset)
			{
				DestroyImmediate(asset);
			}
		}

		private class USPManifestRenderingClass : ScriptableObject
		{
			public const string ASSET_PATH_USPM_DATA = nameof(USPM_DATA);
			[SerializeField]
			public USPManifest USPM_DATA = null;

			public static USPManifestRenderingClass New(USPManifest data)
			{
				var asset = CreateInstance<USPManifestRenderingClass>();
				asset.USPM_DATA = data;
				return asset;
			}

			public static void Delete(USPManifestRenderingClass asset)
			{
				DestroyImmediate(asset);
			}
		}

		/// <summary>
		/// 마지막 호출때 dispose인자에 참을 넣고 호출해주세요
		/// </summary>
		/// <param name="dispose"></param>
		public delegate void OnEditGUIDelegate(bool dispose);

		public static OnEditGUIDelegate OnEditManifestGUI(USPManifest target)
		{
			var asset = USPManifestRenderingClass.New(target);
			var so = new SerializedObject(asset);
			var iter = so.FindPropertyOrFail(USPManifestRenderingClass.ASSET_PATH_USPM_DATA);

			return (dispose) =>
			{
				if (dispose)
				{
					so.ApplyModifiedProperties();
					iter.Dispose();
					so.Dispose();
					USPManifestRenderingClass.Delete(asset);
					return;
				}

				if (target.EDITORGUI__MANIFEST_DATA_FOLDOUT = EditorGUILayout.Foldout(target.EDITORGUI__MANIFEST_DATA_FOLDOUT, "ManifestRawData..."))
				{
					EditorGUILayout.PropertyField(iter, true);
				}

				so.ApplyModifiedProperties();
			};
		}

		public static OnEditGUIDelegate OnEditProjectGUI(USPManifest manifest, SubProjectInfo target)
		{
			var asset = SubProjectRenderingClass.New(target);
			var so = new SerializedObject(asset);

			Stack<IDisposable> dispose_stack = new();
			dispose_stack.Push(so);

			/// <summary>
			/// target의 요소를 바로 쓰면 안되므로 해당 함수를 이용해 값을 변경할것
			/// </summary>
			/// <param name="name"></param>
			/// <returns></returns>
			SerializedProperty GetTargetItem(string name)
			{
				var item = so.FindPropertyOrFail($"{SubProjectRenderingClass.ASSET_PATH_USPM_DATA}.{name}");
				dispose_stack.Push(item);
				return item;
			}

			// 에디터 데이터만
			var foldout_value__project_info = GetTargetItem(nameof(target.EDITORGUI__PROJECT_INFO_FOLDOUT));
			var foldout_value__tools_root = GetTargetItem(nameof(target.EDITORGUI__TOOLS_ROOT_FOLDOUT));
			var foldout_value__import = GetTargetItem(nameof(target.EDITORGUI__IMPORT_FOLDOUT));
			var foldout_value__data = GetTargetItem(nameof(target.EDITORGUI__PROJECT_DATA_FOLDOUT));
			var property_rendering_only = so.FindPropertyOrFail(SubProjectRenderingClass.ASSET_PATH_USPM_DATA);

			return (dispose) =>
			{
				if (dispose)
				{
					so.ApplyModifiedProperties();
					while (dispose_stack.Count > 0) dispose_stack.Pop().Dispose();
					SubProjectRenderingClass.Delete(asset);
					return;
				}

				if (foldout_value__project_info.boolValue = EditorGUILayout.Foldout(foldout_value__project_info.boolValue, $"{target} Infos..."))
				{
					EditorGUILayout.TextField("Project Directory Path", target.ProjectName?.Length > 0 ? target.CurrentProjectDirPath : "<Project name required>");
					EditorGUILayout.TextField("`*.csproj` File Path", target.ProjectName?.Length > 0 ? target.CurrentProjFilePath : "<Project name required>");
				}

				if (foldout_value__tools_root.boolValue = EditorGUILayout.Foldout(foldout_value__tools_root.boolValue, "Tools..."))
				{
					if (GUILayout.Button("Create Project Directory"))
					{
						USPM_Utilities.CreateIfNotFoundCSharpSubProject(target);
					}

					if (foldout_value__import.boolValue = EditorGUILayout.Foldout(foldout_value__import.boolValue, "Import Tool"))
					{
						EditorGUILayout.LabelField($"Add Lib To This USPM Project : {target}");
						EditorGUI.indentLevel++;
						DrawRefs(manifest.UserReferences, target);
						DrawRefs(manifest.UnityProjectHasReferences, target);
						EditorGUI.indentLevel--;

						static void DrawRefs(HashSet<ReferenceInfo> ref_list, SubProjectInfo target)
						{
							using (var lib_info_iter = ref_list.GetEnumerator())
							{
								while (lib_info_iter.MoveNext())
								{
									var lib_info = lib_info_iter.Current;

									EditorGUILayout.LabelField(lib_info.Include);

									EditorGUI.indentLevel++;
									using (var lib_path_iter = lib_info.AbsoluteDLL_HintPathList.GetEnumerator())
									{
										while (lib_path_iter.MoveNext())
										{
											var lib_path = lib_path_iter.Current;

											EditorGUILayout.BeginHorizontal();
											EditorGUILayout.TextField(lib_path);

											EditorGUILayout.LabelField("Import:", GUILayout.Width(100));

											ReferencePreset preset = new(lib_info.Include)
											{
												OptimalSetting = false,
												DLL_Path = lib_path
											};

											if (GUILayout.Button("(Abs)", GUILayout.Width(40)))
											{
												preset.ToRelativePath = false;
												USPM_Utilities.AddDLLToProject(target, preset);
											}
											if (GUILayout.Button("(Rel)", GUILayout.Width(40)))
											{
												preset.ToRelativePath = true;
												USPM_Utilities.AddDLLToProject(target, preset);
											}
											if (GUILayout.Button("(Opt)", GUILayout.Width(40)))
											{
												preset.OptimalSetting = true;
												USPM_Utilities.AddDLLToProject(target, preset);
											}
											EditorGUILayout.EndHorizontal();
										}
									}
									EditorGUI.indentLevel--;
								}
							}
						}
					}
				}

				if (foldout_value__data.boolValue = EditorGUILayout.Foldout(foldout_value__data.boolValue, "ProjectData..."))
				{
					EditorGUILayout.PropertyField(property_rendering_only, true);
				}

				so.ApplyModifiedProperties();
			};
		}

		[Serializable]
		public class SubProjectInfo
		{
			[SerializeField, Header("프로젝트 이름")]
			public string ProjectName = null;
			/// <summary>
			/// 빌드후 유니티 프로젝트로 임포트
			/// </summary>
			[SerializeField, Header("빌드후 유니티에 임포트")]
			public bool ImportIntoUnityProjectAfterBuild = false;
			/// <summary>
			/// 빌드된 DLL을 해당 경로에도 내보낸다
			/// </summary>
			[SerializeField, Header("빌드된 바이너리를 해당 경로에도 출력")]
			public string[] AddOutputPathList = null;
			/// <summary>
			/// 빌드 제외
			/// </summary>
			[SerializeField, Header("해당 프로젝트 빌드를 제외함")]
			public bool BuildExclude = false;
			[SerializeField, Header("구성")]
			public string Configuration = "Debug";

			#region Editor value
			[HideInInspector]
			public bool EDITORGUI__IMPORT_FOLDOUT;
			[HideInInspector]
			public bool EDITORGUI__TOOLS_ROOT_FOLDOUT;
			[HideInInspector]
			public bool EDITORGUI__PROJECT_INFO_FOLDOUT;
			[HideInInspector]
			public bool EDITORGUI__PROJECT_DATA_FOLDOUT;
			[JsonIgnore]
			public string BinaryOutputDirPath => USPM_ConstDataList.GetBaseOutputDirPath(ProjectName);
			#endregion

			public override bool Equals(object obj)
			{
				return obj is SubProjectInfo o && o.CurrentProjFilePath == CurrentProjFilePath;
			}

			public override int GetHashCode()
			{
				return CurrentProjFilePath.GetHashCode();
			}

			public override string ToString()
			{
				return $"Project: {ProjectName}";
			}

			/// <summary>
			/// 현재 프로젝트 디렉터리의 부모 디렉터리 경로
			/// </summary>
			[JsonIgnore]
			public string CurrentProjectParentDirPath => USPM_ConstDataList.USPM_DirPath;
			/// <summary>
			/// 현재 프로젝트 디렉터리 경로
			/// </summary>
			/// <returns></returns>
			[JsonIgnore]
			public string CurrentProjectDirPath => Path.Combine(USPM_ConstDataList.USPM_DirPath, ProjectName);
			/// <summary>
			/// 현재 프로젝트의 CSPROJ파일 경로
			/// </summary>
			/// <returns></returns>
			[JsonIgnore]
			public string CurrentProjFilePath => Path.Combine(CurrentProjectDirPath, $"{ProjectName}.csproj");

			[JsonIgnore]
			public bool HasProjectDirectory
			{
				get
				{
					try
					{
						return Directory.Exists(CurrentProjectDirPath) && File.Exists(CurrentProjFilePath);
					}
					catch
					{
						return false;
					}
				}
			}
		}

		/// <summary>
		/// 현재 매니페스트 이름
		/// </summary>
		[SerializeField]
		public string ThisManifestName = "";
		/// <summary>
		/// * 해당 매니페스트에 포함된 하위 프로젝트 목록
		/// * 빌드시 해당 순서를 따른다. 0부터 시작
		/// </summary>
		[SerializeField]
		public List<SubProjectInfo> ProjectInfos = new();

		/// <summary>
		/// 사용자가 직접 추가한 참조들
		/// </summary> <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		[HideInInspector]
		public HashSet<ReferenceInfo> UserReferences = new();
		/// <summary>
		/// 유니티프로젝트가 지닌 라이브러리들
		/// [include_name,[absolute_path,path]]
		/// </summary>
		/// <returns></returns>
		[HideInInspector]
		public HashSet<ReferenceInfo> UnityProjectHasReferences = new();

		[JsonIgnore]
		public string ThisManifestFilePath => USPM_ConstDataList.USPM_ManifestFilePath;
		[JsonIgnore]
		public string BuildOutputPath => USPM_ConstDataList.USPM_BuildOutputDirPath;

		#region Editor Data

		[HideInInspector]
		private bool EDITORGUI__MANIFEST_DATA_FOLDOUT;

		#endregion

		/// <summary>
		/// 런타임 생성용, 자동 Init됨
		/// </summary>
		/// <param name="manifest_name"></param>
		/// <param name="this_manifest_file_path"></param>
		public USPManifest(string manifest_name)
		{
			ThisManifestName = manifest_name;
		}

		public bool CheckBuildReady()
		{
			static bool IsValidPath(string path)
			{
				char[] i_chars = Path.GetInvalidPathChars();
				foreach (char c in path)
				{
					if (Array.Exists(i_chars, ic => ic == c))
					{
						return false;
					}
				}
				return true;
			}

			if (ProjectInfos.Any(i => i.ProjectName?.Length <= 0))
			{
				Debug.LogError("빌드 할수 없음. 이름이 없거나 문제가 있음");
				return false;
			}
			if (ProjectInfos.Any(i => !IsValidPath(i.CurrentProjFilePath)))
			{
				Debug.LogError("빌드 할수 없음. 경로에 올바르지 않은 문자가 있습니다");
				return false;
			}
			if (ProjectInfos.Any(i => ProjectInfos.Any(j => j != i && j.CurrentProjFilePath == i.CurrentProjFilePath)))
			{
				Debug.LogError("빌드 할수 없음. 이미 동일한 이름의 프로젝트 존재");
				return false;
			}
			for (int i = 0; i < ProjectInfos?.Count; i++)
			{
				if (!ProjectInfos[i].HasProjectDirectory)
				{
					Debug.LogError($"빌드 할수 없음. {ProjectInfos[i]}프로젝트가 준비 되지 않음");
					return false;
				}
			}
			Debug.Log("빌드 가능 확인");
			return true;
		}
	}

	public class USPM_SetupRawENV : AssetPostprocessor
	{
		public static string OnGeneratedSlnSolution(string path, string content)
		{
			return content;
		}

		public static string OnGeneratedCSProject(string path, string content)
		{
			return content;
		}
	}

	[InitializeOnLoad]
	public static class USPM_SetupENV
	{
		static USPM_SetupENV()
		{
			USPM_Utilities.RefreshUSPMInUnitySolution();
		}
	}

	public static class USPM_Utilities
	{
		public static string ExecuteCommand(string command, string arguments,bool no_window = false)
		{
			ProcessStartInfo process_start_info = new ProcessStartInfo
			{
				FileName = command,
				Arguments = arguments,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = no_window,
			};

			using (Process process = new Process())
			{
				process.StartInfo = process_start_info;
				process.Start();
				process.WaitForExit();
				return process.StandardOutput.ReadToEnd();
			}
		}

		public static void RefreshUSPMInUnitySolution()
		{
			Debug.Enable = false;

			var manifest = GetManifest(false);
			if (manifest == null) return;

			for (int i = 0; i < manifest.ProjectInfos?.Count; i++)
			{
				var project = manifest.ProjectInfos[i];
				string command = "dotnet";
				string arguments = $"sln add {project.CurrentProjFilePath}";
				ExecuteCommand(command, arguments,true);
			}

			Debug.Enable = true;

			Debug.Log("USPM Activated.");
		}

		/// <summary>
		/// 주어진 프로젝트 정보가 디렉터리에 없으면 생성
		/// </summary>
		/// <param name="project"></param>
		public static void CreateIfNotFoundCSharpSubProject(USPManifest.SubProjectInfo project)
		{
			if (project.HasProjectDirectory)
			{
				Debug.Log($"Exist Project: {project}");
			}
			else
			{
				string command = "dotnet";
				string arguments = $"new classlib -o \"{Path.Combine(project.CurrentProjectParentDirPath, project.ProjectName)}\"";
				Debug.Log($"New Project Created: {project}, Log:{ExecuteCommand(command, arguments)}");
			}
		}

		private static void CopyDirectory(string source_dir_path, string dest_dir_path)
		{
			if (!Directory.Exists(source_dir_path))
			{
				throw new DirectoryNotFoundException($"소스 디렉터리를 찾을 수 없습니다: {source_dir_path}");
			}

			if (!Directory.Exists(dest_dir_path))
			{
				Directory.CreateDirectory(dest_dir_path);
			}

			foreach (var file in Directory.GetFiles(source_dir_path))
			{
				var source_sub_file_path = Path.GetFileName(file);
				var dest_sub_file_path = Path.Combine(dest_dir_path, source_sub_file_path);
				File.Copy(file, dest_sub_file_path, true);
			}

			foreach (var dir in Directory.GetDirectories(source_dir_path))
			{
				var source_sub_dir_path = Path.GetFileName(dir);
				var dest_sub_dir_path = Path.Combine(dest_dir_path, source_sub_dir_path);
				CopyDirectory(dir, dest_sub_dir_path);
			}
		}

		private static void CopyProjectDLLOnly(USPManifest.SubProjectInfo project, string dest_dir_path)
		{
			foreach(var path in Directory.GetFiles(project.BinaryOutputDirPath, $"{project.ProjectName}.*"))
			{
				File.Copy(path,Path.Combine(dest_dir_path,Path.GetFileName(path)));
			}
		}

		public static void BuildProject(USPManifest.SubProjectInfo project)
		{
			string build_command = "dotnet";
			string build_arguments = $"build \"{project.CurrentProjectDirPath}\" --configuration {project.Configuration} --output \"{project.BinaryOutputDirPath}\"";
			Debug.Log($"Build Executed: {project}, Log: {ExecuteCommand(build_command, build_arguments)}");

			if (project.ImportIntoUnityProjectAfterBuild)
			{
				CopyProjectDLLOnly(project, USPM_ConstDataList.UnityProjectPluginsDirPath);
				//CopyDirectory(project.BinaryOutputDirPath, USPM_ConstDataList.UnityProjectPluginsDirPath);
				AssetDatabase.Refresh();
			}

			foreach (var path in project.AddOutputPathList)
			{
				CopyProjectDLLOnly(project, path);
				//CopyDirectory(project.BinaryOutputDirPath, path);
			}
		}

		private static void CopyDirectory(string binaryOutputDirPath, object unityProjectPluginsDirPath)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// 파일경로의 매니페스트를 가져옴
		/// 만약 경로가 null이고, 파일이 없을 경우 생성됨
		/// </summary>
		/// <param name="create_if_not_exist_manifest_file">존재하지 않을경우 생성</param>
		public static USPManifest GetManifest(bool create_if_not_exist_manifest_file = false)
		{
			var path = USPM_ConstDataList.USPM_ManifestFilePath;
			try
			{
				var o = JsonConvert.DeserializeObject<USPManifest>(File.ReadAllText(path));
				Debug.Log($"Manifest Loaded : {path}");
				return o;
			}
			catch
			{
				if (create_if_not_exist_manifest_file)
				{
					var manifest = new USPManifest($"USPM_ManifestOf_{USPM_ConstDataList.UnityProjectName}");

					SetManifest(manifest);

					Debug.Log($"Manifest Create Loaded : {path}");
					return manifest;
				}
				else return null;
			}
		}

		public static void SetManifest(USPManifest manifest)
		{
			if (!Directory.Exists(USPM_ConstDataList.USPM_DirPath))
			{
				Directory.CreateDirectory(USPM_ConstDataList.USPM_DirPath);
			}

			File.WriteAllText(USPM_ConstDataList.USPM_ManifestFilePath, JsonConvert.SerializeObject(manifest, Unity.Plastic.Newtonsoft.Json.Formatting.Indented));

			Debug.Log($"Manifest Saved");
		}

		public static void AddDLLToProject(USPManifest.SubProjectInfo project, USPManifest.ReferencePreset preset)
		{
			AddDLLToProject(project, new USPManifest.ReferencePreset[] { preset });
		}

		public static void AddDLLToProject(USPManifest.SubProjectInfo project, IEnumerable<USPManifest.ReferencePreset> presets)
		{
			new XML_Processor(project.CurrentProjFilePath,
				new UnityCSharpProjXmlNodeProcessor.SetReferences(presets, project)
			).Processing();
			Debug.Log($"DLL Added.");
		}

		public static void RefreshThisUnityProjectReferenceList(USPManifest m_manifest, bool include_unity_engine, bool include_unity_editor)
		{
			m_manifest.UnityProjectHasReferences.Clear();
			if (include_unity_engine)
			{
				Proc(m_manifest, USPM_ConstDataList.UnityProject_ProjFilePath);
				Debug.Log($"UnityEngine csproj parsed");
			}
			if (include_unity_editor)
			{
				Proc(m_manifest, USPM_ConstDataList.UnityProject_EditorProjFilePath);
				Debug.Log($"UnityEditor csproj parsed");
			}

			Debug.Log($"reference list updated");

			static void Proc(USPManifest manifest, string proj_file_path)
			{
				new XML_Processor(proj_file_path, new UnityCSharpProjXmlNodeProcessor.ParseReferencesInUnityProject(proj_file_path, manifest)).Processing();
			}
		}
	}

	public class USPManager_Window : EditorWindow
	{
		[MenuItem("TANAKADOREI/(USPM)UnitySubProjectManager")]
		public static void ShowWindow()
		{
			var window = GetWindow<USPManager_Window>("USPM");
			window.minSize = new(565, 465);
		}

		public class RefreshTargets
		{
			public static void LoseFocus()
			{
				GameObject temp = new GameObject();
				Selection.activeGameObject = temp;
				Selection.activeGameObject = null;
				DestroyImmediate(temp);
				EditorApplication.RepaintHierarchyWindow();
			}

			Tuple<USPManifest, USPManifest.OnEditGUIDelegate> m_manifest = null;
			Tuple<USPManifest.SubProjectInfo, USPManifest.OnEditGUIDelegate> m_select_project = null;

			public bool ManifestLoaded => m_manifest != null;
			public bool IsSelectedProject => m_select_project != null;

			public RefreshTargets(bool load)
			{
				if (load) ReloadManifest();
			}

			/// <summary>
			/// 파일이 없으면 생성후 로드
			/// </summary>
			public void ReloadManifest()
			{
				if (m_manifest != null)
				{
					m_manifest.Item2(true);
				}
				var manifest = USPM_Utilities.GetManifest(true);
				m_manifest = new(manifest, USPManifest.OnEditManifestGUI(manifest));
				SelectProject(null);
			}

			public void SaveManifest()
			{
				USPM_Utilities.SetManifest(m_manifest.Item1);
			}

			public void ClearAllProjects()
			{
				SelectProject(null);
				m_manifest.Item1.ProjectInfos.Clear();
				SelectProject(null);
			}

			public void TrySaveBuild()
			{
				if (!m_manifest.Item1.CheckBuildReady())
				{
					return;
				}

				SaveManifest();

				for (int i = 0; i < m_manifest.Item1.ProjectInfos?.Count; i++)
				{
					var project = m_manifest.Item1.ProjectInfos[i];
					if (project.BuildExclude)
					{
						Debug.Log($"빌드 제외됨 : {project}");
						continue;
					}
					try
					{
						USPM_Utilities.BuildProject(project);
						Debug.Log($"빌드 완료됨 : {project}");
					}
					catch (Exception e)
					{
						Debug.LogError($"빌드중 런타임 예외 발생. 그리고 중단됨. : {project} : {e}");
					}
				}

				Debug.Log("모든 프로젝트 빌드 성공");
			}

			public void OnGUI_SelectedProjectEditor()
			{
				if (m_select_project == null) return;
				m_select_project.Item2(false);
			}

			public void OnGUI_BaseEditor()
			{
				//m_manifest.Item2(false); 매니페스트 렌더링 부분
				m_manifest.Item1.ThisManifestName = EditorGUILayout.TextField(nameof(m_manifest.Item1.ThisManifestName), m_manifest.Item1.ThisManifestName);
				EditorGUILayout.TextField(nameof(m_manifest.Item1.ThisManifestFilePath), m_manifest.Item1.ThisManifestFilePath);
				EditorGUILayout.LabelField("USPM Projects", m_manifest.Item1.ProjectInfos?.Count.ToString());
				EditorGUILayout.LabelField(nameof(m_manifest.Item1.UserReferences), m_manifest.Item1.UserReferences?.Count.ToString());
				EditorGUILayout.LabelField(nameof(m_manifest.Item1.UnityProjectHasReferences), m_manifest.Item1.UnityProjectHasReferences?.Count.ToString());
				if (GUILayout.Button("[Tool] : Refresh this Unity project references list"))
				{
					USPM_Utilities.RefreshThisUnityProjectReferenceList(m_manifest.Item1, true, true);
				}
				if (GUILayout.Button("[Tool] : USPM Projects Build Check"))
				{
					m_manifest.Item1.CheckBuildReady();
				}
				if (GUILayout.Button("[Tool] : USPM Projects Build"))
				{
					if (m_manifest.Item1.CheckBuildReady())
					{
						Debug.LogWarning("이미 빌드 준비됨");
					}
					else
					{
						for (int i = 0; i < m_manifest.Item1.ProjectInfos?.Count; i++)
						{
							USPM_Utilities.CreateIfNotFoundCSharpSubProject(m_manifest.Item1.ProjectInfos[i]);
						}
						SaveManifest();
						Debug.LogWarning("빌드 준비됨");
					}
				}
			}

			public void OnGUI_ProjectSelector()
			{
				EditorGUILayout.LabelField("SelectProject");
				//bool open = false;
				for (int i = 0; i < m_manifest.Item1.ProjectInfos?.Count; i++)
				{
					// if (i % 4 == 0)
					// {
					// 	if (!open) EditorGUILayout.BeginHorizontal();
					// 	else EditorGUILayout.EndHorizontal();
					// 	open = !open;
					// }

					var project = m_manifest.Item1.ProjectInfos[i];

					if (GUILayout.Button($"[{i}] : {project.ProjectName} : <{(project.BuildExclude ? "BuildExclude" : project.ImportIntoUnityProjectAfterBuild ? "AutoImport" : "Build")}>"))
					{
						SelectProject(project);
						LoseFocus();
					}
				}

				// if (open)
				// {
				// 	EditorGUILayout.EndHorizontal();
				// 	open = false;
				// }
			}

			public bool NewProject()
			{
				try
				{
					var project = new USPManifest.SubProjectInfo();
					m_manifest.Item1.ProjectInfos.Add(project);
				}
				catch (Exception e)
				{
					Debug.LogError(e);
					return false;
				}
				return true;
			}

			public void DeleteSelectProject()
			{
				m_manifest.Item1.ProjectInfos.Remove(m_select_project.Item1);
				SelectProject(null);
			}

			private void SelectProject(USPManifest.SubProjectInfo project)
			{
				if (m_select_project != null)
				{
					m_select_project.Item2(dispose: true); //dispose
				}

				if (project == null)
				{
					m_select_project = null;
				}
				else
				{
					m_select_project = new(project, USPManifest.OnEditProjectGUI(m_manifest.Item1, project));
				}
			}
		}

		RefreshTargets m_refresh_target = null;
		Vector2 m_scroll_vec = Vector2.zero;

		void OnGUI()
		{
			m_scroll_vec = EditorGUILayout.BeginScrollView(m_scroll_vec);
			m_refresh_target ??= new(load: true);
			if (m_refresh_target.ManifestLoaded)
			{
				OnMainGUI();
			}
			else
			{
				OnSetupGUI();
			}
			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns>false일경우 다음 렌터링 호출 요청</returns>
		void OnSetupGUI()
		{
			EditorGUILayout.TextField("HintPath", USPM_ConstDataList.USPM_DirPath);
			if (GUILayout.Button("Setup"))
			{
				m_refresh_target.ReloadManifest();
			}
		}

		void OnMainGUI()
		{
			{
				EditorGUILayout.BeginHorizontal();
				{
					if (GUILayout.Button("ReloadManifest"))
					{
						m_refresh_target.ReloadManifest();
					}
					if (GUILayout.Button("SaveManifest"))
					{
						m_refresh_target.SaveManifest();
					}
				}
				EditorGUILayout.EndHorizontal();
				
				EditorGUILayout.BeginHorizontal();
				{
					if (GUILayout.Button("ClearAllProjects"))
					{
						m_refresh_target.ClearAllProjects();
					}
					if (GUILayout.Button("NewProject"))
					{
						m_refresh_target.NewProject();
					}
					if (GUILayout.Button("DeleteSelectedProject"))
					{
						m_refresh_target.DeleteSelectProject();
					}
				}
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal();
				{
					
					if (GUILayout.Button("Save & BuildAllProjects"))
					{
						m_refresh_target.TrySaveBuild();
					}
					if (GUILayout.Button("Save & UnitySolutionUpdate"))
					{
						m_refresh_target.SaveManifest();
						USPM_Utilities.RefreshUSPMInUnitySolution();
					}
				}
				EditorGUILayout.EndHorizontal();
			}
			DrawSepLine();
			{
				m_refresh_target.OnGUI_BaseEditor();
			}
			DrawSepLine();
			{
				m_refresh_target.OnGUI_ProjectSelector();
			}
			DrawSepLine();
			if (m_refresh_target.IsSelectedProject) m_refresh_target.OnGUI_SelectedProjectEditor();
		}

		public static void DrawSepLine()
		{
			EditorGUILayout.Space(16);
			EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 2), Color.gray);
			EditorGUILayout.Space(16);
		}
	}
}

#endif

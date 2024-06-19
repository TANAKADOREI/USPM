using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Codice.CM.Common.Serialization.Replication;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
/// <summary>
/// UnitySubProjectManagement (USPM)
/// </summary>
namespace TANAKADOREI.Unity.Editor.USPM
{
	public static class USPM_ConstDataList
	{
		public const string USPM = "USPM";
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
		public static string USPM_DirPath => Path.Combine(UnityProjectDirPath, USPM);
		/// <summary>
		/// USPM 매니페스트 파일 경로
		/// `ParentDir/MyUnityProject/USPM/MANIFEST.json`
		/// </summary>
		/// <returns></returns>
		public static string USPM_ManifestFilePath => Path.Combine(USPM_DirPath, "MANIFEST.json");
		/// <summary>
		/// 현재 유니티 프로젝트 이름
		/// `MyUnityProject`
		/// </summary>
		/// <returns></returns>
		public static string UnityProjectName => Path.GetFileName(UnityProjectDirPath);
		/// <summary>
		/// Assembly-CSharp.csproj 파일 경로
		/// </summary>
		/// <returns></returns>
		public static string UnityProject_ProjFilePath => Path.Combine(UnityProjectDirPath, "Assembly-CSharp.csproj");
		/// <summary>
		/// Assembly-CSharp-Editor.csproj 파일 경로
		/// </summary>
		/// <returns></returns>
		public static string UnityProject_EditorProjFilePath => Path.Combine(UnityProjectDirPath, "Assembly-CSharp-Editor.csproj");
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
		//[CreateAssetMenu(fileName = "SubProjectInfo.asset", menuName = "SubProjectInfoAsset")]
		private class Asset : ScriptableObject
		{
			[SerializeField]
			public SubProjectInfo ManifestInfo = new();

			public static Asset New(SubProjectInfo data)
			{
				var asset = CreateInstance<Asset>();
				asset.ManifestInfo = data;
				return asset;
			}

			public static void Delete(Asset asset)
			{
				DestroyImmediate(asset);
			}
		}

		/// <summary>
		/// 마지막 호출때 dispose인자에 참을 넣고 호출해주세요
		/// </summary>
		/// <param name="dispose"></param>
		public delegate void OnEditGUIDelegate(bool dispose);

		public static OnEditGUIDelegate OnEditProjectGUI(USPManifest manifest, SubProjectInfo target)
		{
			var asset = Asset.New(target);
			var so = new SerializedObject(asset);
			// var iter = so.GetIterator();
			var iter = so.FindPropertyOrFail("ManifestInfo");

			// if (!iter.NextVisible(true))
			// {
			// 	throw new Exception("프로젝트 읽기 실패");
			// }

			return (dispose) =>
			{
				if (dispose)
				{
					so.ApplyModifiedProperties();
					iter.Dispose();
					so.Dispose();
					Asset.Delete(asset);
					return;
				}

				EditorGUILayout.LabelField("Selected Project Info...");
				EditorGUILayout.TextField("Project Directory Path", target.CurrentProjectDirPath);
				EditorGUILayout.TextField("`*.csproj` File Path", target.CurrentProjFilePath);

				EditorGUILayout.PropertyField(iter, true);
				so.ApplyModifiedProperties();
			};
		}

		[Serializable]
		public class SubProjectInfo
		{
			[SerializeField, Header("프로젝트 이름")]
			public string ProjectName = null;
			[SerializeField, Header("`<Project>/<PropertyGroup>/<AssemblyName>`에 강제 삽입됨.")]
			public string AssemblyName = null;
			[SerializeField, Header("`<Project>/<PropertyGroup>/<RootNamespace>`에 강제 삽입됨.")]
			public string RootNamespace = null;
			/// <summary>
			/// 유니티 csproj의 Reference Include=""의 어트리뷰트값
			/// </summary>
			[SerializeField, Header("현재 유니티 프로젝트의 참조 라이브러리에서 `<Reference Include=\"\">`의 어트리뷰트와 비교해 찾으면, `<Project>/<ItemGroup>/`에 강제 삽입됨.")]
			public string[] References = null;
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
				return $"Project: {ProjectName}, Assembly: {AssemblyName}";
			}

			/// <summary>
			/// 현재 프로젝트 디렉터리 경로
			/// </summary>
			/// <returns></returns>
			public string CurrentProjectDirPath => Path.Combine(USPM_ConstDataList.USPM_DirPath, ProjectName);

			/// <summary>
			/// 현재 프로젝트의 CSPROJ파일 경로
			/// </summary>
			/// <returns></returns>
			public string CurrentProjFilePath => Path.Combine(CurrentProjectDirPath, $"{ProjectName}.csproj");

			public bool IsBuildReady => Directory.Exists(CurrentProjectDirPath) && File.Exists(CurrentProjFilePath);
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

		public string ThisManifestFilePath => USPM_ConstDataList.USPM_ManifestFilePath;

		/// <summary>
		/// 런타임 생성용, 자동 Init됨
		/// </summary>
		/// <param name="manifest_name"></param>
		/// <param name="this_manifest_file_path"></param>
		public USPManifest(string manifest_name)
		{
			ThisManifestName = manifest_name;
		}

		public bool IsBuildReady()
		{
			for (int i = 0; i < ProjectInfos?.Count; i++)
			{
				if (!ProjectInfos[i].IsBuildReady) return false;
			}
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
			// 유니티 에디터가 로드될 때 호출될 코드
			EditorApplication.delayCall += OnUnityLoaded;
		}

		/// <summary>
		/// 유니티 에디터 로딩 완료
		/// </summary>
		private static void OnUnityLoaded()
		{
		}
	}

	public static class USPM_Utilities
	{
		public static string ExecuteCommand(string command, string arguments)
		{
			ProcessStartInfo process_start_info = new ProcessStartInfo
			{
				FileName = command,
				Arguments = arguments,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = false
			};

			using (Process process = new Process())
			{
				process.StartInfo = process_start_info;
				process.Start();
				process.WaitForExit();
				return process.StandardOutput.ReadToEnd();
			}
		}

		// public static void AddToUnitySolution(USPManifest.SubProjectInfo project)
		// {
		// 	string command = "dotnet";
		// 	string arguments = $"sln add {project.CurrentProjFilePath}";
		// 	ExecuteCommand(command, arguments);
		// }

		/// <summary>
		/// 주어진 프로젝트 정보가 디렉터리에 없으면 생성
		/// </summary>
		/// <param name="project"></param>
		public static void CreateIfNotFoundCSharpSubProject(USPManifest.SubProjectInfo project)
		{
			string command = "dotnet";
			string arguments = $"new classlib -o \"{Path.Combine(project.CurrentProjectDirPath, project.ProjectName)}\"";
			Debug.LogWarning($"새 프로젝트 생성됨: {project.ProjectName}, Log:{ExecuteCommand(command, arguments)}");
		}

		/// <summary>
		/// 루트 
		/// </summary>
		/// <param name="project">null일경우 루트</param>
		public static void BuildProject(USPManifest.SubProjectInfo project)
		{
			string build_command = "dotnet";
			string build_arguments = $"build \"{project.CurrentProjectDirPath}\"";
			Debug.LogWarning($"빌드: {project.ProjectName}, Log: {ExecuteCommand(build_command, build_arguments)}");
			AssetDatabase.Refresh();
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
				return o;
			}
			catch
			{
				if (create_if_not_exist_manifest_file)
				{
					var manifest = new USPManifest($"USPM_ManifestOf_{USPM_ConstDataList.UnityProjectName}");

					SetManifest(manifest);

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

			File.WriteAllText(USPM_ConstDataList.USPM_ManifestFilePath, JsonConvert.SerializeObject(manifest));
		}
	}

	public class USPManager_Window : EditorWindow
	{

		[MenuItem("TANAKADOREI/USPManager")]
		public static void ShowWindow()
		{
			var window = GetWindow<USPManager_Window>("UnitySubProjectManager(USPM)");
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

			USPManifest m_manifest = null;
			Tuple<USPManifest.SubProjectInfo, USPManifest.OnEditGUIDelegate> m_select_project = null;

			public bool ManifestLoaded => m_manifest != null;
			public bool IsSelectedProject => m_select_project != null;

			public RefreshTargets(bool load)
			{
				if (load) Refresh();
			}

			/// <summary>
			/// 파일이 없으면 생성후 로드
			/// </summary>
			public void Refresh()
			{
				m_manifest = USPM_Utilities.GetManifest(true);
				SelectProject(null);
			}

			public void Save()
			{
				USPM_Utilities.SetManifest(m_manifest);
			}

			public void Reset()
			{
				SelectProject(null);
				m_manifest.ProjectInfos.Clear();
				SelectProject(null);
			}

			public void TrySaveBuild()
			{
				if (!m_manifest.IsBuildReady())
				{
					Debug.LogError("빌드 할수 없음. 프로젝트가 준비되어 있는지 확인하세요");
					return;
				}
				Save();
				Refresh();

				for (int i = 0; i < m_manifest.ProjectInfos?.Count; i++)
				{
					var project = m_manifest.ProjectInfos[i];
					if (project.BuildExclude)
					{
						Debug.LogWarning($"빌드 제외됨 : {project}");
						continue;
					}
					try
					{
						USPM_Utilities.BuildProject(project);
						Debug.LogWarning($"빌드 완료됨 : {project}");
					}
					catch (Exception e)
					{
						Debug.LogError($"빌드중 런타임 예외 발생. 그리고 중단됨. : {project} : {e}");
					}
				}

				Debug.LogWarning("모든 프로젝트 빌드 성공");
			}

			public void OnGUI_SelectedProjectEditor()
			{
				if (m_select_project == null) return;
				m_select_project.Item2(false);
			}

			public void OnGUI_BaseEditor()
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

				m_manifest.ThisManifestName = EditorGUILayout.TextField(nameof(m_manifest.ThisManifestName), m_manifest.ThisManifestName);
				EditorGUILayout.TextField(nameof(m_manifest.ThisManifestFilePath), m_manifest.ThisManifestFilePath);
				EditorGUILayout.LabelField("Projects", m_manifest.ProjectInfos?.Count.ToString());
				if (GUILayout.Button("[Tool] : Import an already existing project"))
				{
					//todo
				}
				if (GUILayout.Button("[Tool] : Projects Build Ready Check"))
				{
					try
					{
						Debug.Log(m_manifest.IsBuildReady() ? $"빌드 준비됨" : $"빌드 준비 안됨");
					}
					catch (Exception e)
					{
						Debug.Log($"빌드 준비 안됨. 예외로 인한. {e}");
					}
				}
				if (GUILayout.Button("[Tool] : ProjectsBuildSetting"))
				{
					if (m_manifest.ProjectInfos.Any(i => i.ProjectName?.Length <= 0))
					{
						Debug.LogError("빌드 할수 없음. 이름이 없거나 문제가 있음");
						goto skip;
					}
					if (m_manifest.ProjectInfos.Any(i => !IsValidPath(i.CurrentProjFilePath)))
					{
						Debug.LogError("빌드 할수 없음. 경로에 올바르지 않은 문자가 있습니다");
						goto skip;
					}
					if (m_manifest.ProjectInfos.Any(i => m_manifest.ProjectInfos.Any(j => j != i && j.CurrentProjFilePath == i.CurrentProjFilePath)))
					{
						Debug.LogError("빌드 할수 없음. 이미 동일한 이름의 프로젝트 존재");
						goto skip;
					}

					if (m_manifest.IsBuildReady())
					{
						Debug.LogWarning("이미 빌드 준비됨");
					}
					else
					{
						for (int i = 0; i < m_manifest.ProjectInfos?.Count; i++)
						{
							USPM_Utilities.CreateIfNotFoundCSharpSubProject(m_manifest.ProjectInfos[i]);
						}
						Debug.LogWarning("빌드 준비됨");
					}

				skip:;
				}
			}

			public void OnGUI_ProjectSelector()
			{
				EditorGUILayout.LabelField("SelectProject");
				//bool open = false;
				for (int i = 0; i < m_manifest.ProjectInfos?.Count; i++)
				{
					// if (i % 4 == 0)
					// {
					// 	if (!open) EditorGUILayout.BeginHorizontal();
					// 	else EditorGUILayout.EndHorizontal();
					// 	open = !open;
					// }

					var project = m_manifest.ProjectInfos[i];

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
					m_manifest.ProjectInfos.Add(project);
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
				m_manifest.ProjectInfos.Remove(m_select_project.Item1);
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
					m_select_project = new(project, USPManifest.OnEditProjectGUI(m_manifest, project));
				}
			}
		}

		RefreshTargets m_refresh_target = null;

		void OnGUI()
		{
			m_refresh_target ??= new(load: true);
			if (m_refresh_target.ManifestLoaded)
			{
				OnMainGUI();
			}
			else
			{
				OnSetupGUI();
			}
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
				m_refresh_target.Refresh();
			}
		}

		void OnMainGUI()
		{
			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("ResetUSPM"))
				{
					m_refresh_target.Reset();
				}
				if (GUILayout.Button("Build"))
				{
					m_refresh_target.TrySaveBuild();
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
				if (GUILayout.Button("ReloadManifest"))
				{
					m_refresh_target.Refresh();
				}
				if (GUILayout.Button("SaveManifest"))
				{
					m_refresh_target.Save();
				}
			}
			EditorGUILayout.EndHorizontal();

			m_refresh_target.OnGUI_BaseEditor();
			m_refresh_target.OnGUI_ProjectSelector();
			if (m_refresh_target.IsSelectedProject) m_refresh_target.OnGUI_SelectedProjectEditor();
		}
	}
}

#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
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
		public static string USPM_RootManifestFilePath => Path.Combine(USPM_DirPath, "MANIFEST.json");
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
		[Serializable]
		public class SubProjectInfo
		{
			[SerializeField]
			public string ProjectName = null;
			[SerializeField]
			public string AssemblyName = null;
			[SerializeField]
			public string RootNamespace = null;
			/// <summary>
			/// 유니티 csproj의 Reference Include=""의 어트리뷰트값
			/// </summary>
			[SerializeField]
			public string[] References = null;
			/// <summary>
			/// 빌드후 유니티 프로젝트로 임포트
			/// </summary>
			[SerializeField]
			public bool ImportIntoUnityProjectAfterBuild = false;
			/// <summary>
			/// 빌드된 DLL을 해당 경로에도 내보낸다
			/// </summary>
			[SerializeField]
			public string[] AddOutputPathList = null;
			/// <summary>
			/// 빌드 제외
			/// </summary>
			[SerializeField]
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
				return CurrentProjFilePath;
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
		/// 런타임 생성용, 자동 Init됨
		/// </summary>
		/// <param name="manifest_name"></param>
		/// <param name="this_manifest_file_path"></param>
		public USPManifest(string manifest_name)
		{
			ThisManifestName = manifest_name;
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
		public static void ExecuteCommand(string command, string arguments)
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
				string result = process.StandardOutput.ReadToEnd();
				Debug.Log($"ExecuteCommand : {result}");
			}
		}

		public static void AddToUnitySolution(USPManifest.SubProjectInfo project)
		{
			string command = "dotnet";
			string arguments = $"sln add {project.CurrentProjFilePath}";
			ExecuteCommand(command, arguments);
		}

		/// <summary>
		/// 주어진 프로젝트 정보가 디렉터리에 없으면 생성
		/// </summary>
		/// <param name="project"></param>
		public static void CreateIfNotFoundSubProject(USPManifest.SubProjectInfo project)
		{
			string command = "dotnet";
			string arguments = $"new classlib -o \"{Path.Combine(project.CurrentProjectDirPath, project.ProjectName)}\"";
			ExecuteCommand(command, arguments);
		}

		/// <summary>
		/// 루트 
		/// </summary>
		/// <param name="project">null일경우 루트</param>
		public static void BuildProject(USPManifest.SubProjectInfo project = null)
		{
			string build_command = "dotnet";
			string build_arguments = $"build \"{project.CurrentProjectDirPath}\"";
			ExecuteCommand(build_command, build_arguments);
			AssetDatabase.Refresh();
		}

		/// <summary>
		/// 파일경로의 매니페스트를 가져옴
		/// 만약 경로가 null이고, 파일이 없을 경우 생성됨
		/// </summary>
		/// <param name="create_if_not_exist_manifest_file">존재하지 않을경우 생성</param>
		public static USPManifest GetManifest(bool create_if_not_exist_manifest_file = false)
		{
			var path = USPM_ConstDataList.USPM_RootManifestFilePath;
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

			File.WriteAllText(USPM_ConstDataList.USPM_RootManifestFilePath, JsonConvert.SerializeObject(manifest));
		}
	}

	public class USPManager_Window : EditorWindow
	{

		[MenuItem("TANAKADOREI/USPManager")]
		public static void ShowWindow()
		{
			GetWindow<USPManager_Window>("UnitySubProjectManager(USPM)");
		}

		USPManifest m_manifest = null;
		int m_selected_sub_project_index = 0;
		GUIContent[] m_display_sub_project_name = null;

		void RefreshManifest()
		{
			m_manifest = USPM_Utilities.GetManifest(true);
			m_selected_sub_project_index = 0;
			m_display_sub_project_name = m_manifest.ProjectInfos.Select(i => new GUIContent($"\"{i.ProjectName}\" - \"{i.AssemblyName}\"")).ToArray();
		}

		void SaveManifest()
		{
			if (m_manifest != null) USPM_Utilities.SetManifest(m_manifest);
		}

		void OnGUI()
		{
			if (m_manifest != null)
			{
				OnMainMenuGUI();
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
			if (GUILayout.Button("Setup"))
			{
				RefreshManifest();
			}
		}

		void OnMainMenuGUI()
		{
			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("ResetUSPM"))
				{
				}
				if (GUILayout.Button("Build"))
				{
				}
			}
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("ReloadManifest"))
				{
				}
				if (GUILayout.Button("SaveManifest"))
				{
				}
			}
			EditorGUILayout.EndHorizontal();
			m_selected_sub_project_index = EditorGUILayout.Popup(new("ProjectSetting"), m_selected_sub_project_index, m_display_sub_project_name);
		}

		void OnMainGUI()
		{

		}
	}
}

#endif

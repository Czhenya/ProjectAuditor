using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.ProjectAuditor.Editor.Auditors;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_2018_1_OR_NEWER
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

#endif

namespace Unity.ProjectAuditor.Editor
{
    /// <summary>
    /// ProjectAuditor class is responsible for auditing the Unity project
    /// </summary>
    public class ProjectAuditor
#if UNITY_2018_1_OR_NEWER
        : IPreprocessBuildWithReport
#endif
    {
        private static string m_DataPath;

        private readonly List<IAuditor> m_Auditors = new List<IAuditor>();

        /// <summary>
        /// ProjectAuditor constructor
        /// </summary>
        public ProjectAuditor()
        {
            const string path = "Assets/Editor";
            const string assetFilename = "ProjectAuditorConfig.asset";
            var assetPath = Path.Combine(path, assetFilename);
            config = AssetDatabase.LoadAssetAtPath<ProjectAuditorConfig>(assetPath);
            if (config == null)
            {
                if (!File.Exists(path)) Directory.CreateDirectory(path);
                config = ScriptableObject.CreateInstance<ProjectAuditorConfig>();
                AssetDatabase.CreateAsset(config, assetPath);
            }

            foreach (var type in AssemblyHelper.GetAllTypesInheritedFromInterface<IAuditor>())
            {
                var instance = Activator.CreateInstance(type) as IAuditor;
                instance.Initialize(config);
                instance.Reload(dataPath);
                m_Auditors.Add(instance);
            }
        }

        public ProjectAuditorConfig config { get; set; }

        private static string dataPath
        {
            get
            {
                if (string.IsNullOrEmpty(m_DataPath))
                {
                    const string path = "Packages/com.unity.project-auditor/Data";
                    if (!File.Exists(Path.GetFullPath(path)))
                    {
                        // if it's not a package, let's search through all assets
                        var apiDatabasePath = AssetDatabase.GetAllAssetPaths()
                            .FirstOrDefault(p => p.EndsWith("Data/ApiDatabase.json"));

                        if (string.IsNullOrEmpty(apiDatabasePath))
                            throw new Exception("Could not find ApiDatabase.json");
                        m_DataPath = apiDatabasePath.Substring(0, apiDatabasePath.IndexOf("/ApiDatabase.json"));
                    }
                }

                return m_DataPath;
            }
        }

        /// <summary>
        /// Runs all available auditors (code, project settings) and generate a report of all found issues.
        /// </summary>
        /// <param name="progressBar"> Progress bar, if applicable </param>
        /// <returns> Generated report </returns>
        public ProjectReport Audit(IProgressBar progressBar = null)
        {
            var projectReport = new ProjectReport();
            var completed = false;

            Audit(projectReport.AddIssue, (_completed) => { completed = _completed; }, progressBar);

            while (!completed)
                Thread.Sleep(50);
            return projectReport;
        }

        /// <summary>
        /// Runs all available auditors (code, project settings) and generate a report of all found issues.
        /// </summary>
        /// <param name="onIssueFound"> Action called whenever a new issue is found </param>
        /// <param name="onUpdate"> Action called whenever an internal auditor completes </param>
        /// <param name="progressBar"> Progress bar, if applicable </param>
        public void Audit(Action<ProjectIssue> onIssueFound, Action<bool> onUpdate, IProgressBar progressBar = null)
        {
            var stopwatch = Stopwatch.StartNew();

            var numAuditors = m_Auditors.Count;
            foreach (var auditor in m_Auditors)
            {
                var startTime = stopwatch.ElapsedMilliseconds;
                auditor.Audit(onIssueFound, () =>
                {
                    if (config.logTimingsInfo) Debug.Log(auditor.GetType().Name + " took: " + (stopwatch.ElapsedMilliseconds - startTime) / 1000.0f + " seconds.");

                    onUpdate(false);

                    numAuditors--;
                    // check if all auditors completed
                    if (numAuditors == 0)
                    {
                        stopwatch.Stop();
                        if (config.logTimingsInfo)
                            Debug.Log("Project Auditor took: " + stopwatch.ElapsedMilliseconds / 1000.0f + " seconds.");

                        onUpdate(true);
                    }
                }, progressBar);
            }
            Debug.Log("Project Auditor time to interactive: " + stopwatch.ElapsedMilliseconds / 1000.0f + " seconds.");
        }

        public T GetAuditor<T>() where T : class
        {
            foreach (var iauditor in m_Auditors)
            {
                var auditor = iauditor as T;
                if (auditor != null)
                    return auditor;
            }

            return null;
        }

        public void Reload(string path)
        {
            foreach (var auditor in m_Auditors) auditor.Reload(path);
        }

#if UNITY_2018_1_OR_NEWER
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (config.enableAnalyzeOnBuild)
            {
                var projectReport = Audit();

                var numIssues = projectReport.NumTotalIssues;
                if (numIssues > 0)
                {
                    if (config.enableFailBuildOnIssues)
                        Debug.LogError("Project Auditor found " + numIssues + " issues");
                    else
                        Debug.Log("Project Auditor found " + numIssues + " issues");
                }
            }
        }

#endif
    }
}

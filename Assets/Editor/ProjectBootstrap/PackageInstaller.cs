using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectBootstrap
{
    // Installs (and optionally removes) a fixed set of packages via the PackageManager
    // Client API, headless-safe.
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.netcode.gameobjects@2.13.3",
            "com.unity.services.multiplayer@2.3.3",
            "com.unity.services.vivox@16.12.1",
            "com.unity.multiplayer.playmode@2.0.2",
        };

        // Template defaults this prototype does not use.
        static readonly string[] PackagesToRemove =
        {
            "com.unity.visualscripting",
            "com.unity.collab-proxy",
        };

        const double TimeoutSeconds = 900; // UPM resolution + downloads can be slow

        static AddAndRemoveRequest _request;
        static double _deadline;

        // Invoke with: -executeMethod ProjectBootstrap.PackageInstaller.Install  (NO -quit)
        public static void Install()
        {
            Debug.Log($"[PackageInstaller] Adding: {string.Join(", ", PackagesToAdd)}");
            _request = Client.AddAndRemove(packagesToAdd: PackagesToAdd, packagesToRemove: PackagesToRemove);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_request == null) return;

            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;

            if (_request.Status == StatusCode.Success)
            {
                var names = _request.Result.Select(p => $"{p.name}@{p.version}");
                Debug.Log($"[PackageInstaller] Resolved: {string.Join(", ", names)}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[PackageInstaller] Failed: {_request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}

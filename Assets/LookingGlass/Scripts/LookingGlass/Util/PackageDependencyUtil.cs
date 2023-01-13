#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.Threading.Tasks;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LookingGlass {
    [InitializeOnLoad]
    public class PackageDependencyUtil {
        private const string NewtonsoftJsonPackageName = "com.unity.nuget.newtonsoft-json";

        static PackageDependencyUtil() {
            _ = AutoInstallNewtonsoftJson();
        }

        private static async Task<bool> AutoInstallNewtonsoftJson() {
            ListRequest listRequest = Client.List(false, true);
            PackageCollection packages = await UPMUtility.AwaitRequest(listRequest);

            bool found = false;
            foreach (PackageInfo p in packages) {
                if (p.name == NewtonsoftJsonPackageName) {
                    found = true;
                    break;
                }
            }

            if (found)
                return false;

            string packageId = NewtonsoftJsonPackageName + "@2.0.0";
            Debug.Log("Installing " + packageId + " because the Looking Glass Plugin depends on it...");
            AddRequest addRequest = Client.Add(packageId);
            PackageInfo result = await UPMUtility.AwaitRequest(addRequest);
            return true;
        }
    }
}
#endif
